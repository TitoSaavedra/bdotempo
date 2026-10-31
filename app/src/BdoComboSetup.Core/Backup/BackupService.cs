using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Files;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Backup;

public sealed record BackupTarget(string Pcid, string SourcePath);

/// <summary>
/// One character file inside a backup set. <see cref="RelativePath"/> is relative to the backup folder with forward slashes
/// (<c>account/pcid/gameVariable.xml</c>, as in the Python tool). <see cref="Sha256"/> is lowercase hex.
/// </summary>
public sealed record BackupFile(string Pcid, string RelativePath, string Sha256, long Size);

public sealed record BackupSet(
    string Name,
    string FolderPath,
    string Account,
    string Reason,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<BackupFile> Files);

/// <summary>A backup folder for the list. Unreadable manifests are listed too, with <see cref="Readable"/> false.</summary>
public sealed record BackupSummary(string Name, string? Reason, string? Account, int Characters, string? ToolVersion, bool Readable);

public sealed record RestoreItem(string Pcid, string DestinationPath, bool CurrentIdentical);

public sealed record RestoreResult(
    OperationOutcome Outcome,
    OperationProblem Problem,
    string? ProblemDetail,
    IReadOnlyList<RestoreItem> Items,
    string? PreRestoreBackup);

public interface IBackupService
{
    /// <summary>Copies each target into a new backup set and verifies every copy by hash. Throws BackupFailed on any error.</summary>
    BackupSet Create(string account, IReadOnlyList<BackupTarget> targets, string reason);

    IReadOnlyList<BackupSummary> List();

    /// <summary>Restores files from a backup set. Dry run unless <paramref name="write"/> is set.</summary>
    RestoreResult Restore(string backupName, IReadOnlyList<string> pcids, bool write, bool forceWhileGameRuns);

    /// <summary>Copies one character file of a backup back to <paramref name="destinationPath"/> and verifies it by hash.</summary>
    void RestoreFile(string backupName, string pcid, string destinationPath);
}

/// <summary>
/// Backup sets in the layout of the Python tool (<c>src\bdo_combo\backup.py</c>):
/// <c>&lt;backupsRoot&gt;\&lt;UTC yyyyMMddTHHmmssZ&gt;\manifest.json</c> and
/// <c>&lt;backupsRoot&gt;\&lt;UTC stamp&gt;\&lt;account&gt;\&lt;pcid&gt;\gameVariable.xml</c>. The manifest keys are the Python ones:
/// tool, tool_version, created_utc, reason, account and files[pcid, path, sha256, size]. The manifest is written last,
/// so a set without one is never restored from.
/// Sets written by the earlier C# build (<c>&lt;stamp&gt;-reason\&lt;pcid&gt;\</c>, camelCase keys) are still listed and restored.
/// </summary>
public sealed class BackupService : IBackupService
{
    public const string ManifestName = "manifest.json";
    public const string ToolName = "bdo-combo";
    public const string ToolVersion = "0.1.0";

    /// <summary>The Python folder name, with the optional "Z" and "-reason" suffix of the earlier C# build still accepted.</summary>
    private static readonly Regex FolderNameRe = new("\\A[0-9]{8}T[0-9]{6}Z?(-[a-z0-9][a-z0-9-]*)?\\z");
    private static readonly Regex ReasonRe = new("\\A[a-z0-9][a-z0-9-]*\\z");
    private static readonly Regex Sha256Re = new("\\A[0-9a-f]{64}\\z");
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Indented like Python's json.dumps. Relaxed escaping keeps "/" and "+" readable, as Python writes them.</summary>
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Func<string> _backupsRootOf;
    private readonly GameDataLocator _locator;
    private readonly IFileStore _files;
    private readonly IGameProcessGuard _guard;
    private readonly AppLogger _logger;
    private readonly Func<DateTimeOffset> _clock;

    public BackupService(
        string backupsRoot,
        GameDataLocator locator,
        IFileStore files,
        IGameProcessGuard guard,
        AppLogger logger,
        Func<DateTimeOffset>? clock = null)
        : this(() => backupsRoot, locator, files, guard, logger, clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupsRoot);
    }

    /// <param name="backupsRoot">Read on every operation, so a change of the backups folder in the settings applies at once.</param>
    public BackupService(
        Func<string> backupsRoot,
        GameDataLocator locator,
        IFileStore files,
        IGameProcessGuard guard,
        AppLogger logger,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(backupsRoot);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(logger);

        _backupsRootOf = backupsRoot;
        _locator = locator;
        _files = files;
        _guard = guard;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string BackupsRoot => _backupsRootOf();

    public static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public BackupSet Create(string account, IReadOnlyList<BackupTarget> targets, string reason)
    {
        using var scope = _logger.BeginOperation("backup.create", new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["files"] = targets.Count,
        });

        try
        {
            if (targets.Count == 0)
            {
                throw new OperationRefusedException(OperationProblem.BackupFailed, "nothing to back up");
            }

            if (!ReasonRe.IsMatch(reason))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, "backup reason must be a short lowercase slug");
            }

            if (!GameDataLocator.IsDigits(account))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, "account must be numeric");
            }

            if (targets.Any(target => !GameDataLocator.IsDigits(target.Pcid)))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, "the character id must be digits");
            }

            var folder = ReserveFolder();
            var name = Path.GetFileName(folder);
            try
            {
                var files = new List<BackupFile>();
                foreach (var target in targets)
                {
                    var original = _files.ReadAllBytes(target.SourcePath);
                    var originalHash = Sha256Hex(original);
                    var destination = CopyFilePath(folder, account, target.Pcid);
                    _files.WriteAllBytes(destination, original);

                    if (Sha256Hex(_files.ReadAllBytes(destination)) != originalHash)
                    {
                        throw new OperationRefusedException(
                            OperationProblem.BackupFailed,
                            "a backup copy of a character does not match the original");
                    }

                    files.Add(new BackupFile(target.Pcid, RelativeCopyPath(account, target.Pcid), originalHash, original.LongLength));
                }

                var set = new BackupSet(name, folder, account, reason, _clock().ToUniversalTime(), files);
                WriteManifest(folder, set);
                VerifyFolder(folder);
                _logger.Info("backup.created", "Backup created and verified", new Dictionary<string, object?>
                {
                    ["backup"] = name,
                    ["files"] = files.Count,
                    ["reason"] = reason,
                });
                return set;
            }
            catch (Exception)
            {
                RemovePartialSet(folder);
                throw;
            }
        }
        catch (OperationRefusedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            throw new OperationRefusedException(OperationProblem.BackupFailed, $"backup failed: {ex.Message}");
        }
    }

    public IReadOnlyList<BackupSummary> List()
    {
        using var scope = _logger.BeginOperation("backup.list");
        if (!Directory.Exists(BackupsRoot))
        {
            return [];
        }

        var summaries = new List<BackupSummary>();
        foreach (var folder in Directory.EnumerateDirectories(BackupsRoot).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(folder);
            if (!FolderNameRe.IsMatch(name))
            {
                continue;
            }

            try
            {
                var manifest = ReadManifest(folder);
                summaries.Add(new BackupSummary(name, manifest.Reason, manifest.Account, manifest.Files.Count, manifest.ToolVersion, true));
            }
            catch (OperationRefusedException ex)
            {
                _logger.Warn("backup.manifest_unreadable", "Unreadable backup manifest", new Dictionary<string, object?>
                {
                    ["backup"] = name,
                    ["error"] = ex.Message,
                });
                summaries.Add(new BackupSummary(name, null, null, 0, null, false));
            }
        }

        return summaries;
    }

    public RestoreResult Restore(string backupName, IReadOnlyList<string> pcids, bool write, bool forceWhileGameRuns)
    {
        using var scope = _logger.BeginOperation("backup.restore", new Dictionary<string, object?>
        {
            ["backup"] = backupName,
            ["write"] = write,
            ["force"] = forceWhileGameRuns,
        });

        try
        {
            var folder = ResolveBackupFolder(backupName);
            var manifest = ReadManifest(folder);
            if (_locator.ExplicitAccount is not null && _locator.ExplicitAccount != manifest.Account)
            {
                throw new OperationRefusedException(
                    OperationProblem.InvalidRequest,
                    $"backup {backupName} belongs to account {manifest.Account}, not {_locator.ExplicitAccount}");
            }

            var entries = manifest.Files.ToDictionary(f => f.Pcid, StringComparer.Ordinal);
            var selected = pcids.Count > 0 ? pcids.Distinct(StringComparer.Ordinal).ToList() : entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (var pcid in selected)
            {
                if (!entries.ContainsKey(pcid))
                {
                    throw new OperationRefusedException(OperationProblem.InvalidRequest, $"the character is not in backup {backupName}");
                }
            }

            var items = new List<RestoreItem>();
            var payloads = new List<(string Pcid, string Destination, byte[] Data)>();
            foreach (var pcid in selected)
            {
                var data = ReadVerifiedCopy(folder, manifest, entries[pcid]);
                var destination = _locator.CharacterFilePath(manifest.Account, pcid);
                if (!_files.Exists(destination))
                {
                    throw new OperationRefusedException(OperationProblem.CharacterNotFound, $"character {pcid} not found under account {manifest.Account}");
                }

                var identical = _files.ReadAllBytes(destination).SequenceEqual(data);
                items.Add(new RestoreItem(pcid, destination, identical));
                payloads.Add((pcid, destination, data));
            }

            if (!write)
            {
                return new RestoreResult(OperationOutcome.DryRun, OperationProblem.None, null, items, null);
            }

            var targets = payloads
                .Select(p => new GuardTarget(p.Pcid, p.Destination, _files.GetLastWriteUtc(p.Destination)))
                .ToList();
            WriteGuard.Check(_guard, targets, forceWhileGameRuns, _clock().UtcDateTime, _logger);

            var preRestore = Create(
                manifest.Account,
                payloads.Select(p => new BackupTarget(p.Pcid, p.Destination)).ToList(),
                "pre-restore");

            foreach (var payload in payloads)
            {
                _files.AtomicReplace(payload.Destination, payload.Data);
                if (Sha256Hex(_files.ReadAllBytes(payload.Destination)) != entries[payload.Pcid].Sha256)
                {
                    throw new OperationRefusedException(
                        OperationProblem.WriteFailed,
                        $"restore verification failed for a character; the previous state is in backup {preRestore.Name}");
                }
            }

            _logger.Info("backup.restored", "Backup restored", new Dictionary<string, object?>
            {
                ["backup"] = backupName,
                ["files"] = payloads.Count,
                ["pre_restore"] = preRestore.Name,
            });
            return new RestoreResult(OperationOutcome.Written, OperationProblem.None, null, items, preRestore.Name);
        }
        catch (OperationRefusedException ex)
        {
            _logger.Warn("backup.restore_refused", ex.Message, new Dictionary<string, object?> { ["backup"] = backupName });
            return new RestoreResult(OperationOutcome.Refused, ex.Problem, ex.Message, [], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            return new RestoreResult(OperationOutcome.Failed, OperationProblem.UnexpectedError, ex.Message, [], null);
        }
    }

    public void RestoreFile(string backupName, string pcid, string destinationPath)
    {
        using var scope = _logger.BeginOperation("backup.restore_file", new Dictionary<string, object?>
        {
            ["backup"] = backupName,
            ["pcid"] = pcid,
        });

        var folder = ResolveBackupFolder(backupName);
        var manifest = ReadManifest(folder);
        var entry = manifest.Files.FirstOrDefault(f => f.Pcid == pcid)
            ?? throw new OperationRefusedException(OperationProblem.InvalidRequest, $"the character is not in backup {backupName}");

        var data = ReadVerifiedCopy(folder, manifest, entry);
        _files.AtomicReplace(destinationPath, data);
        if (Sha256Hex(_files.ReadAllBytes(destinationPath)) != entry.Sha256)
        {
            throw new OperationRefusedException(OperationProblem.RollbackFailed, "a restored file does not match the backup");
        }
    }

    private string ResolveBackupFolder(string backupName)
    {
        if (!FolderNameRe.IsMatch(backupName))
        {
            throw new OperationRefusedException(OperationProblem.InvalidRequest, "backup must be a backup folder name such as 20261031T120000Z");
        }

        var folder = Path.Combine(BackupsRoot, backupName);
        if (!Directory.Exists(folder))
        {
            throw new OperationRefusedException(OperationProblem.InvalidRequest, $"backup {backupName} not found");
        }

        return folder;
    }

    /// <summary>Creates the folder of the current UTC second, or of the next free second. Existing backups are never overwritten.</summary>
    private string ReserveFolder()
    {
        Directory.CreateDirectory(BackupsRoot);
        var start = _clock().ToUniversalTime();
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var stamp = start.AddSeconds(attempt).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var folder = Path.Combine(BackupsRoot, stamp);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        throw new OperationRefusedException(OperationProblem.BackupFailed, "could not reserve a new backup folder");
    }

    private static string CopyFilePath(string folder, string account, string pcid) =>
        Path.Combine(folder, account, pcid, GameDataLocator.CharacterFileName);

    private static string RelativeCopyPath(string account, string pcid) => $"{account}/{pcid}/{GameDataLocator.CharacterFileName}";

    /// <summary>
    /// Where a manifest entry's copy lies. The Python layout is <c>account\pcid\gameVariable.xml</c>; the earlier C# layout
    /// was <c>pcid\gameVariable.xml</c>. Any other path is refused.
    /// </summary>
    private static string ResolveCopyPath(string folder, Manifest manifest, ManifestEntry entry)
    {
        if (entry.RelativePath is null || entry.RelativePath == RelativeCopyPath(manifest.Account, entry.Pcid))
        {
            return CopyFilePath(folder, manifest.Account, entry.Pcid);
        }

        if (entry.RelativePath == $"{entry.Pcid}/{GameDataLocator.CharacterFileName}")
        {
            return Path.Combine(folder, entry.Pcid, GameDataLocator.CharacterFileName);
        }

        throw new OperationRefusedException(
            OperationProblem.BackupFailed,
            $"manifest of backup {Path.GetFileName(folder)} has an unexpected path for a character");
    }

    /// <summary>Reads one copy and refuses it unless its SHA-256 matches the manifest.</summary>
    private byte[] ReadVerifiedCopy(string folder, Manifest manifest, ManifestEntry entry)
    {
        var data = _files.ReadAllBytes(ResolveCopyPath(folder, manifest, entry));
        if (Sha256Hex(data) != entry.Sha256)
        {
            throw new OperationRefusedException(OperationProblem.BackupFailed, "a backup file does not match its manifest");
        }

        return data;
    }

    /// <summary>Re-reads the manifest and every copy of a set just written.</summary>
    private void VerifyFolder(string folder)
    {
        var manifest = ReadManifest(folder);
        foreach (var entry in manifest.Files)
        {
            ReadVerifiedCopy(folder, manifest, entry);
        }
    }

    /// <summary>
    /// Writes the manifest like the Python tool: indented, CRLF line ends, keys in the same order.
    /// </summary>
    private void WriteManifest(string folder, BackupSet set)
    {
        var manifest = new ManifestDto
        {
            Tool = ToolName,
            ToolVersion = ToolVersion,
            CreatedUtc = set.CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "+00:00",
            Reason = set.Reason,
            Account = set.Account,
            Files = set.Files.Select(f => new ManifestFileDto
            {
                Pcid = f.Pcid,
                RelativePath = f.RelativePath,
                Sha256 = f.Sha256,
                Size = f.Size,
            }).ToList(),
        };

        var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions).ReplaceLineEndings("\r\n") + "\r\n";
        _files.WriteAllBytes(Path.Combine(folder, ManifestName), Utf8NoBom.GetBytes(json));
    }

    /// <summary>
    /// Reads and checks a manifest. Accepts the Python keys and the camelCase keys of the earlier C# build
    /// (toolVersion). Throws BackupFailed when it is missing, unreadable or invalid.
    /// </summary>
    private Manifest ReadManifest(string folder)
    {
        var name = Path.GetFileName(folder);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(Encoding.UTF8.GetString(_files.ReadAllBytes(Path.Combine(folder, ManifestName))))?.AsObject()
                ?? throw new InvalidOperationException("the manifest is not a JSON object");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new OperationRefusedException(OperationProblem.BackupFailed, $"cannot read manifest of backup {name}: {ex.Message}");
        }

        OperationRefusedException Invalid(string detail) =>
            new(OperationProblem.BackupFailed, $"manifest of backup {name} {detail}");

        var account = Text(root, "account");
        if (account is null || !GameDataLocator.IsDigits(account))
        {
            throw Invalid("has no valid account");
        }

        if (root["files"] is not JsonArray filesNode)
        {
            throw Invalid("has no 'files' list");
        }

        var entries = new List<ManifestEntry>();
        foreach (var node in filesNode)
        {
            if (node is not JsonObject item)
            {
                throw Invalid("has an invalid file entry");
            }

            var pcid = Text(item, "pcid");
            var sha256 = Text(item, "sha256");
            if (pcid is null || !GameDataLocator.IsDigits(pcid) || sha256 is null || !Sha256Re.IsMatch(sha256))
            {
                throw Invalid("has an invalid file entry");
            }

            entries.Add(new ManifestEntry(pcid, Text(item, "path"), sha256));
        }

        if (entries.Select(e => e.Pcid).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw Invalid("lists a character more than once");
        }

        return new Manifest(account, Text(root, "tool_version", "toolVersion"), Text(root, "reason"), entries);
    }

    private static string? Text(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text;
            }
        }

        return null;
    }

    private void RemovePartialSet(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("backup.partial_cleanup_failed", "Could not remove the incomplete backup folder", ex, new Dictionary<string, object?>
            {
                ["backup"] = Path.GetFileName(folder),
            });
        }
    }

    /// <summary>A manifest as read. Only the fields the service uses.</summary>
    private sealed record Manifest(string Account, string? ToolVersion, string? Reason, IReadOnlyList<ManifestEntry> Files);

    /// <summary>One file entry. <see cref="RelativePath"/> is null when the manifest has no path.</summary>
    private sealed record ManifestEntry(string Pcid, string? RelativePath, string Sha256);

    private sealed class ManifestDto
    {
        [JsonPropertyName("tool")]
        public string Tool { get; set; } = string.Empty;

        [JsonPropertyName("tool_version")]
        public string ToolVersion { get; set; } = string.Empty;

        [JsonPropertyName("created_utc")]
        public string CreatedUtc { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("account")]
        public string Account { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<ManifestFileDto> Files { get; set; } = [];
    }

    private sealed class ManifestFileDto
    {
        [JsonPropertyName("pcid")]
        public string Pcid { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
