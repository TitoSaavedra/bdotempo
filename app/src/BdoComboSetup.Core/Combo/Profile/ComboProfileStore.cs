using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Stores profiles as JSON in one folder (%LOCALAPPDATA%\BDOTempo\profiles by default), named
/// "classType.specId.json". Saves go through a temporary file, so a crash never leaves a half-written file.
/// A file that cannot be read is logged and treated as "no profile"; the base guide is then shown.
/// </summary>
public sealed class ComboProfileStore(string directory, AppLogger logger) : IComboProfileStore
{
    /// <summary>The schema version this code reads and writes. Other versions are ignored and logged.</summary>
    public const int SupportedSchema = 1;

    private static readonly Regex SpecPattern = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BDOTempo", "profiles");

    /// <summary>The file of one class and spec. The spec id must be a plain lowercase name, because it becomes part of a file name.</summary>
    public string PathFor(int classType, string specId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specId);
        if (!SpecPattern.IsMatch(specId))
        {
            throw new ArgumentException("The spec id must be lowercase letters, digits, hyphens or underscores.", nameof(specId));
        }

        return Path.Combine(_directory, $"{classType.ToString(CultureInfo.InvariantCulture)}.{specId}.json");
    }

    public async Task<ComboProfile?> LoadAsync(int classType, string specId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(classType, specId);
        using var scope = _logger.BeginOperation("combo.profile.load", new Dictionary<string, object?>
        {
            ["class_type"] = classType,
            ["spec"] = specId,
        });

        if (!File.Exists(path))
        {
            _logger.Debug("combo.profile.none", "No saved profile for this guide");
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var json = await JsonSerializer.DeserializeAsync<ProfileJson>(stream, ReadOptions, cancellationToken);
            if (json is null || json.Schema != SupportedSchema)
            {
                _logger.Warn("combo.profile.schema", "The saved profile has an unsupported schema and is ignored", new Dictionary<string, object?>
                {
                    ["schema"] = json?.Schema,
                });
                return null;
            }

            if (json.ClassType != classType || !string.Equals(json.Spec, specId, StringComparison.Ordinal))
            {
                _logger.Warn("combo.profile.mismatch", "The saved profile belongs to another guide and is ignored");
                return null;
            }

            var profile = ToRecord(json);
            _logger.Info("combo.profile.loaded", "Saved profile read", new Dictionary<string, object?>
            {
                ["chains"] = profile.Chains.Count,
                ["steps"] = profile.Chains.Sum(chain => chain.Steps.Count),
            });
            return profile;
        }
        catch (JsonException ex)
        {
            scope.Fail(ex);
            _logger.Error("combo.profile.invalid", "The saved profile is not valid JSON and is ignored", ex);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            _logger.Error("combo.profile.read_failed", "The saved profile could not be read and is ignored", ex);
            return null;
        }
    }

    public async Task SaveAsync(ComboProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var path = PathFor(profile.ClassType, profile.SpecId);
        using var scope = _logger.BeginOperation("combo.profile.save", new Dictionary<string, object?>
        {
            ["class_type"] = profile.ClassType,
            ["spec"] = profile.SpecId,
            ["chains"] = profile.Chains.Count,
            ["steps"] = profile.Chains.Sum(chain => chain.Steps.Count),
        });

        try
        {
            Directory.CreateDirectory(_directory);
            var temporary = path + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, ToJson(profile), WriteOptions, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            throw;
        }
    }

    public bool Delete(int classType, string specId)
    {
        var path = PathFor(classType, specId);
        using var scope = _logger.BeginOperation("combo.profile.delete", new Dictionary<string, object?>
        {
            ["class_type"] = classType,
            ["spec"] = specId,
        });

        if (!File.Exists(path))
        {
            _logger.Info("combo.profile.delete.none", "No saved profile to delete");
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            throw;
        }
    }

    private static ComboProfile ToRecord(ProfileJson json) =>
        new(
            json.ClassType,
            json.Spec ?? string.Empty,
            (json.Chains ?? []).Select(chain => new ComboProfileChain(
                chain.Chain,
                (chain.Steps ?? []).Select(step => new ComboProfileStep(
                    step.Base,
                    step.SkillNo,
                    step.CustomName,
                    step.Memo,
                    step.CooldownSlot,
                    step.QuickPosition)).ToList())).ToList());

    private static ProfileJson ToJson(ComboProfile profile) =>
        new()
        {
            Schema = SupportedSchema,
            ClassType = profile.ClassType,
            Spec = profile.SpecId,
            Chains = profile.Chains.Select(chain => new ProfileChainJson
            {
                Chain = chain.Chain,
                Steps = chain.Steps.Select(step => new ProfileStepJson
                {
                    Base = step.BaseStep,
                    SkillNo = step.SkillNo,
                    CustomName = step.CustomName,
                    Memo = step.Memo,
                    CooldownSlot = step.CooldownSlot,
                    QuickPosition = step.QuickPosition,
                }).ToList(),
            }).ToList(),
        };

    // JSON shapes of the profile file (schema 1). Private: the public types are the records above.
    private sealed class ProfileJson
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; }

        [JsonPropertyName("classType")]
        public int ClassType { get; set; }

        [JsonPropertyName("spec")]
        public string? Spec { get; set; }

        [JsonPropertyName("chains")]
        public List<ProfileChainJson>? Chains { get; set; }
    }

    private sealed class ProfileChainJson
    {
        [JsonPropertyName("chain")]
        public int Chain { get; set; }

        [JsonPropertyName("steps")]
        public List<ProfileStepJson>? Steps { get; set; }
    }

    private sealed class ProfileStepJson
    {
        [JsonPropertyName("base")]
        public int? Base { get; set; }

        [JsonPropertyName("skillNo")]
        public int? SkillNo { get; set; }

        [JsonPropertyName("customName")]
        public string? CustomName { get; set; }

        [JsonPropertyName("memo")]
        public string? Memo { get; set; }

        [JsonPropertyName("cooldownSlot")]
        public int? CooldownSlot { get; set; }

        [JsonPropertyName("quickPosition")]
        public int? QuickPosition { get; set; }
    }
}
