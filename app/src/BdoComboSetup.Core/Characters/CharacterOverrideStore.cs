using System.Text.Json;
using System.Text.Json.Serialization;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Characters;

/// <summary>What the user set for one character: a name and/or a class. Null means "not set" (the roster decides).</summary>
public sealed record CharacterOverride(string? Name, int? ClassType);

/// <summary>
/// The user's own names and classes for the characters, per account, in %LOCALAPPDATA%\BdoComboSetup\characters.json.
/// Only this file is written by the app for characters; the game files are never touched. Every change is saved at once
/// (temporary file, then move). A file that cannot be read is moved aside and the store starts empty.
/// </summary>
public sealed class CharacterOverrideStore
{
    public const int MaxNameLength = 40;
    private const int FormatVersion = 1;
    private const string CorruptSuffix = ".corrupt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly AppLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, OverrideRecord>> _accounts = new(StringComparer.Ordinal);

    public CharacterOverrideStore(string path, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        _path = path;
        _logger = logger;
        Load();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BdoComboSetup", "characters.json");

    /// <summary>The override of a character, or null when the user set nothing for it.</summary>
    public CharacterOverride? Get(string account, string pcid)
    {
        ValidateKeys(account, pcid);
        lock (_gate)
        {
            return _accounts.TryGetValue(account, out var characters) && characters.TryGetValue(pcid, out var record)
                ? new CharacterOverride(record.Name, record.ClassType)
                : null;
        }
    }

    /// <summary>Sets the override of a character and saves. Both null removes it. The name is trimmed and cut to 40 characters.</summary>
    public void Set(string account, string pcid, CharacterOverride? value)
    {
        ValidateKeys(account, pcid);
        lock (_gate)
        {
            var name = NormalizeName(value?.Name);
            var classType = value?.ClassType;
            if (name is null && classType is null)
            {
                if (_accounts.TryGetValue(account, out var existing))
                {
                    existing.Remove(pcid);
                    if (existing.Count == 0)
                    {
                        _accounts.Remove(account);
                    }
                }
            }
            else
            {
                if (!_accounts.TryGetValue(account, out var characters))
                {
                    characters = new Dictionary<string, OverrideRecord>(StringComparer.Ordinal);
                    _accounts[account] = characters;
                }

                characters[pcid] = new OverrideRecord { Name = name, ClassType = classType };
            }

            Save();
        }
    }

    private static void ValidateKeys(string account, string pcid)
    {
        if (!GameDataLocator.IsDigits(account) || !GameDataLocator.IsDigits(pcid))
        {
            throw new ArgumentException("account and character id must be digits");
        }
    }

    internal static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }

    private void Load()
    {
        using var scope = _logger.BeginOperation("characters.store.load", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(_path),
        });

        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_path));
            if (file?.Accounts is null)
            {
                return;
            }

            foreach (var (account, characters) in file.Accounts)
            {
                if (!GameDataLocator.IsDigits(account) || characters is null)
                {
                    continue;
                }

                var copy = new Dictionary<string, OverrideRecord>(StringComparer.Ordinal);
                foreach (var (pcid, record) in characters)
                {
                    if (GameDataLocator.IsDigits(pcid) && record is not null)
                    {
                        copy[pcid] = new OverrideRecord { Name = NormalizeName(record.Name), ClassType = record.ClassType };
                    }
                }

                if (copy.Count > 0)
                {
                    _accounts[account] = copy;
                }
            }

            _logger.Info("characters.store.loaded", "Character overrides loaded", new Dictionary<string, object?>
            {
                ["accounts"] = _accounts.Count,
                ["characters"] = _accounts.Values.Sum(characters => characters.Count),
            });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            MoveCorruptAside();
        }
    }

    private void MoveCorruptAside()
    {
        var aside = _path + CorruptSuffix;
        try
        {
            File.Move(_path, aside, overwrite: true);
            _logger.Warn("characters.store.corrupt", "characters.json could not be read; it was moved aside and the list starts empty", new Dictionary<string, object?>
            {
                ["moved_to"] = Path.GetFileName(aside),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("characters.store.move_failed", "characters.json could not be read nor moved aside", ex);
        }
    }

    private void Save()
    {
        using var scope = _logger.BeginOperation("characters.store.save", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(_path),
        });

        var file = new StoreFile
        {
            Version = FormatVersion,
            Accounts = _accounts.ToDictionary(
                account => account.Key,
                account => account.Value.ToDictionary(character => character.Key, character => character.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
        };
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            scope.Fail(ex);
            throw;
        }
    }

    private sealed class StoreFile
    {
        public int Version { get; set; } = FormatVersion;

        public Dictionary<string, Dictionary<string, OverrideRecord>>? Accounts { get; set; }
    }

    private sealed class OverrideRecord
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("classType")]
        public int? ClassType { get; set; }
    }
}
