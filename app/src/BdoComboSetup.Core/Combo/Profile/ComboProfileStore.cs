using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Stores the user's custom combos as JSON in one folder (%LOCALAPPDATA%\BDOTempo\profiles by default), one file per
/// class and spec named "classType.specId.json". A file holds the ten slots of that class and spec, each with its own
/// copy of the chains, its optional name and the time it was saved. Saves go through a temporary file, so a crash never
/// leaves a half-written file. A file that cannot be read is logged and treated as "no slots"; the base guide is then shown.
/// <para>
/// A schema 1 file (the old layout, one combo per class and spec) is read as slot 1 and upgraded the next time a slot is
/// saved. The bdoclasshub guide is never written by this store.
/// </para>
/// <para>
/// Access to the files is serialized with one lock, so two saves of the same class and spec cannot lose each other's
/// slots (the whole read-modify-write of the file is guarded; the files are small and every call is synchronous).
/// </para>
/// </summary>
public sealed class ComboProfileStore(string directory, AppLogger logger) : IComboProfileStore
{
    /// <summary>The schema version this code reads and writes. Other versions are ignored and logged.</summary>
    public const int SupportedSchema = 2;

    /// <summary>The schema of the old single-combo files; they are read as slot 1.</summary>
    public const int LegacySchema = 1;

    private static readonly Regex SpecPattern = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly object _gate = new();

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

    public Task<IReadOnlyList<ComboProfileSlot>> ListSlotsAsync(int classType, string specId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _logger.BeginOperation("combo.profile.list", Fields(classType, specId));

        IReadOnlyList<ComboProfileSlot> slots;
        lock (_gate)
        {
            slots = ReadSlots(PathFor(classType, specId), classType, specId) ?? [];
        }
        _logger.Info("combo.profile.listed", "Saved combos listed", Fields(classType, specId, new Dictionary<string, object?>
        {
            ["slots"] = slots.Count,
        }));

        return Task.FromResult(slots);
    }

    public Task<ComboProfileSlot?> GetSlotAsync(int classType, string specId, int slot, CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot);
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _logger.BeginOperation("combo.profile.get", Fields(classType, specId, slot));

        ComboProfileSlot? saved;
        lock (_gate)
        {
            saved = ReadSlots(PathFor(classType, specId), classType, specId)?.FirstOrDefault(item => item.Slot == slot);
        }

        if (saved is null)
        {
            _logger.Debug("combo.profile.slot.none", "The slot is empty");
        }

        return Task.FromResult(saved);
    }

    public Task<ComboProfileSlot> SaveSlotAsync(
        int classType,
        string specId,
        int slot,
        string? name,
        IReadOnlyList<ComboProfileChain> chains,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chains);
        ValidateSlot(slot);
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _logger.BeginOperation("combo.profile.save", Fields(classType, specId, slot, chains));

        try
        {
            lock (_gate)
            {
                return Task.FromResult(SaveSlot(classType, specId, slot, name, chains));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            throw;
        }
    }

    public bool DeleteSlot(int classType, string specId, int slot)
    {
        ValidateSlot(slot);
        var path = PathFor(classType, specId);
        using var scope = _logger.BeginOperation("combo.profile.delete", Fields(classType, specId, slot));

        try
        {
            lock (_gate)
            {
                var slots = ReadSlots(path, classType, specId);
                if (slots is null || !slots.Any(item => item.Slot == slot))
                {
                    _logger.Info("combo.profile.delete.none", "No saved combo in this slot to delete");
                    return false;
                }

                var remaining = slots.Where(item => item.Slot != slot).ToList();
                if (remaining.Count == 0)
                {
                    File.Delete(path);
                }
                else
                {
                    WriteSlots(path, classType, specId, remaining);
                }

                _logger.Info("combo.profile.deleted", "Saved combo deleted", Fields(classType, specId, new Dictionary<string, object?>
                {
                    ["slot"] = slot,
                    ["remaining"] = remaining.Count,
                }));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            throw;
        }
    }

    public Task<ComboProfile?> LoadAsync(int classType, string specId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _logger.BeginOperation("combo.profile.load", Fields(classType, specId));

        ComboProfileSlot? saved;
        lock (_gate)
        {
            saved = ReadSlots(PathFor(classType, specId), classType, specId)?
                .FirstOrDefault(item => item.Slot == ComboProfileSlot.FirstSlot);
        }

        if (saved is null)
        {
            return Task.FromResult<ComboProfile?>(null);
        }

        _logger.Info("combo.profile.loaded", "Saved combo read", Fields(classType, specId, new Dictionary<string, object?>
        {
            ["slot"] = saved.Slot,
            ["chains"] = saved.Chains.Count,
            ["steps"] = saved.Chains.Sum(chain => chain.Steps.Count),
        }));
        return Task.FromResult<ComboProfile?>(saved.ToProfile());
    }

    public Task SaveAsync(ComboProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _logger.BeginOperation("combo.profile.save", Fields(profile.ClassType, profile.SpecId, ComboProfileSlot.FirstSlot, profile.Chains));

        try
        {
            lock (_gate)
            {
                // The legacy save only replaces the chains, so the name of slot 1 is kept when it has one.
                var name = ReadSlots(PathFor(profile.ClassType, profile.SpecId), profile.ClassType, profile.SpecId)?
                    .FirstOrDefault(item => item.Slot == ComboProfileSlot.FirstSlot)?.Name;
                SaveSlot(profile.ClassType, profile.SpecId, ComboProfileSlot.FirstSlot, name, profile.Chains);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            throw;
        }
    }

    public bool Delete(int classType, string specId) => DeleteSlot(classType, specId, ComboProfileSlot.FirstSlot);

    /// <summary>Reads the file, replaces one slot and writes it back. The caller holds <see cref="_gate"/>.</summary>
    private ComboProfileSlot SaveSlot(int classType, string specId, int slot, string? name, IReadOnlyList<ComboProfileChain> chains)
    {
        var path = PathFor(classType, specId);
        var slots = ReadSlots(path, classType, specId);
        if (slots is null && File.Exists(path))
        {
            // The file exists but could not be read: its other slots are lost, so say it loudly before overwriting it.
            _logger.Warn("combo.profile.unreadable_overwrite", "A file that could not be read is replaced by the new slot", Fields(classType, specId, slot));
        }

        var saved = new ComboProfileSlot(classType, specId, slot, NormalizeName(name), DateTimeOffset.UtcNow, chains);
        var merged = (slots ?? []).Where(item => item.Slot != slot).Append(saved).OrderBy(item => item.Slot).ToList();
        WriteSlots(path, classType, specId, merged);

        _logger.Info("combo.profile.saved", "Combo saved", Fields(classType, specId, slot, chains));
        return saved;
    }

    /// <summary>
    /// Reads the slots of the file. Null means there is no file or it cannot be used (missing, damaged, another guide or
    /// an unsupported schema); the reason is always logged. A schema 1 file comes back as a single slot 1.
    /// The caller holds <see cref="_gate"/>.
    /// </summary>
    private IReadOnlyList<ComboProfileSlot>? ReadSlots(string path, int classType, string specId)
    {
        if (!File.Exists(path))
        {
            _logger.Debug("combo.profile.none", "No saved combos for this guide");
            return null;
        }

        try
        {
            var json = JsonSerializer.Deserialize<ProfileFileJson>(File.ReadAllText(path), ReadOptions);
            if (json is null)
            {
                WarnSchema(null);
                return null;
            }

            if (json.Schema != LegacySchema && json.Schema != SupportedSchema)
            {
                WarnSchema(json.Schema);
                return null;
            }

            if (json.Schema == SupportedSchema && json.Slots is null)
            {
                WarnSchema(json.Schema);
                return null;
            }

            if (json.ClassType != classType || !string.Equals(json.Spec, specId, StringComparison.Ordinal))
            {
                _logger.Warn("combo.profile.mismatch", "The saved combos belong to another guide and are ignored");
                return null;
            }

            // The file time is the best guess for a save time that is missing from the file.
            var fallback = new DateTimeOffset(DateTime.SpecifyKind(File.GetLastWriteTimeUtc(path), DateTimeKind.Utc));
            if (json.Schema == LegacySchema)
            {
                _logger.Info("combo.profile.legacy", "A schema 1 combo was read as slot 1", Fields(classType, specId, ComboProfileSlot.FirstSlot));
                return [new ComboProfileSlot(classType, specId, ComboProfileSlot.FirstSlot, null, fallback, ToChains(json.Chains))];
            }

            return ParseSlots(json.Slots ?? [], classType, specId, fallback);
        }
        catch (JsonException ex)
        {
            _logger.Error("combo.profile.invalid", "The saved combos are not valid JSON and are ignored", ex);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("combo.profile.read_failed", "The saved combos could not be read and are ignored", ex);
            return null;
        }
    }

    private IReadOnlyList<ComboProfileSlot> ParseSlots(
        List<ProfileSlotJson> slots,
        int classType,
        string specId,
        DateTimeOffset fallback)
    {
        var result = new SortedDictionary<int, ComboProfileSlot>();
        foreach (var item in slots.OfType<ProfileSlotJson>())
        {
            if (!ComboProfileSlot.IsValidSlot(item.Slot))
            {
                _logger.Warn("combo.profile.slot_range", "A saved combo slot is out of range and is ignored", new Dictionary<string, object?>
                {
                    ["slot"] = item.Slot,
                });
                continue;
            }

            if (result.ContainsKey(item.Slot))
            {
                _logger.Warn("combo.profile.slot_duplicate", "A saved combo slot is listed twice; the first one is used", new Dictionary<string, object?>
                {
                    ["slot"] = item.Slot,
                });
                continue;
            }

            result[item.Slot] = new ComboProfileSlot(
                classType,
                specId,
                item.Slot,
                NormalizeName(item.Name),
                ParseSavedAt(item.SavedAt, fallback),
                ToChains(item.Chains));
        }

        return result.Values.ToList();
    }

    /// <summary>Writes every slot of the file through a temporary file, so a crash never leaves it half-written.</summary>
    private void WriteSlots(string path, int classType, string specId, IEnumerable<ComboProfileSlot> slots)
    {
        Directory.CreateDirectory(_directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(ToJson(classType, specId, slots), WriteOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static List<ComboProfileChain> ToChains(List<ProfileChainJson>? chains) =>
        (chains ?? []).OfType<ProfileChainJson>().Select(chain => new ComboProfileChain(
            chain.Chain,
            (chain.Steps ?? []).OfType<ProfileStepJson>().Select(step => new ComboProfileStep(
                step.Base,
                step.SkillNo,
                step.CustomName,
                step.Memo,
                step.CooldownSlot,
                step.QuickPosition)).ToList())).ToList();

    /// <summary>The file layout: slots in slot order and chains in guide order, so the same combo always writes the same bytes.</summary>
    private static ProfileFileJson ToJson(int classType, string specId, IEnumerable<ComboProfileSlot> slots) =>
        new()
        {
            Schema = SupportedSchema,
            ClassType = classType,
            Spec = specId,
            Slots = slots.OrderBy(slot => slot.Slot).Select(slot => new ProfileSlotJson
            {
                Slot = slot.Slot,
                Name = NormalizeName(slot.Name),
                SavedAt = slot.SavedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Chains = slot.Chains.OrderBy(chain => chain.Chain).Select(chain => new ProfileChainJson
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
            }).ToList(),
        };

    private static DateTimeOffset ParseSavedAt(string? savedAt, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(savedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : fallback;

    private static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > ComboProfileSlot.MaxNameLength ? trimmed[..ComboProfileSlot.MaxNameLength] : trimmed;
    }

    private static void ValidateSlot(int slot)
    {
        if (!ComboProfileSlot.IsValidSlot(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "The slot must be between 1 and 10.");
        }
    }

    private void WarnSchema(int? schema) =>
        _logger.Warn("combo.profile.schema", "The saved combos use an unsupported schema and are ignored", new Dictionary<string, object?>
        {
            ["schema"] = schema,
        });

    private static Dictionary<string, object?> Fields(int classType, string specId, int slot, IReadOnlyList<ComboProfileChain>? chains = null)
    {
        var fields = Fields(classType, specId);
        fields["slot"] = slot;
        if (chains is not null)
        {
            fields["chains"] = chains.Count;
            fields["steps"] = chains.Sum(chain => chain.Steps.Count);
        }

        return fields;
    }

    private static Dictionary<string, object?> Fields(int classType, string specId) => new(StringComparer.Ordinal)
    {
        ["class_type"] = classType,
        ["spec"] = specId,
    };

    private static Dictionary<string, object?> Fields(int classType, string specId, Dictionary<string, object?> extra)
    {
        var fields = Fields(classType, specId);
        foreach (var (key, value) in extra)
        {
            fields[key] = value;
        }

        return fields;
    }

    // JSON shapes of the profile file. Private: the public types are the records. Schema 2 holds "slots"; schema 1 held
    // one combo in "chains" and is only read.
    private sealed class ProfileFileJson
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; }

        [JsonPropertyName("classType")]
        public int ClassType { get; set; }

        [JsonPropertyName("spec")]
        public string? Spec { get; set; }

        [JsonPropertyName("slots")]
        public List<ProfileSlotJson>? Slots { get; set; }

        [JsonPropertyName("chains")]
        public List<ProfileChainJson>? Chains { get; set; }
    }

    private sealed class ProfileSlotJson
    {
        [JsonPropertyName("slot")]
        public int Slot { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("savedAt")]
        public string? SavedAt { get; set; }

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
