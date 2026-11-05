using System.Text.Json;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Combo;

/// <summary>One entry of a combo file (schema 2) with its game slot already assigned from the layout.</summary>
public sealed record ComboEntry(
    string Group,
    int Slot,
    string GuideName,
    string SkillName,
    int SkillNo,
    string? Memo,
    bool Optional);

/// <summary>A parsed combo file (port of the Python tool's load_combo). Layout values are inclusive game slot ranges.</summary>
public sealed record ComboFile(
    string Name,
    string ClassName,
    int ClassId,
    string Spec,
    string Source,
    string Description,
    IReadOnlyDictionary<string, (int First, int Last)> Layout,
    IReadOnlyList<ComboEntry> Entries);

/// <summary>
/// Reads combo files (schema 2) and turns them into cooldown slots. The layout names three groups in fixed order:
/// combo (slots), priorityBuffs, otherBuffs. Entries fill their range from its first slot.
/// </summary>
public static class ComboFileReader
{
    public const int ComboSchema = 2;
    public const int SlotCount = 20;

    private static readonly string[] GroupOrder = ["combo", "priorityBuffs", "otherBuffs"];
    private static readonly Dictionary<string, string> GroupArrays = new(StringComparer.Ordinal)
    {
        ["combo"] = "slots",
        ["priorityBuffs"] = "priorityBuffs",
        ["otherBuffs"] = "otherBuffs",
    };

    private static readonly Regex OverrideRe = new("\\A([0-9]+)=([0-9]+)\\z");

    public static ComboFile Read(string path, AppLogger logger)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        using var scope = logger.BeginOperation("combo.load", new Dictionary<string, object?> { ["combo"] = name });
        try
        {
            if (!File.Exists(path))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"combo file not found: {Path.GetFileName(path)}");
            }

            using var document = ParseDocument(path);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("combo file must contain a JSON object");
            }

            if (!root.TryGetProperty("schema", out var schemaElement) || !TryInt(schemaElement, out var schema) || schema != ComboSchema)
            {
                throw Invalid($"unsupported combo schema (expected {ComboSchema})");
            }

            if (!root.TryGetProperty("classId", out var classElement) || !TryInt(classElement, out var classId))
            {
                throw Invalid("combo classId must be an integer");
            }

            var layout = ParseLayout(root);
            var entries = new List<ComboEntry>();
            foreach (var group in GroupOrder)
            {
                entries.AddRange(ParseEntries(root, group, layout));
            }

            if (!entries.Any(e => e.Group == "combo"))
            {
                throw Invalid("combo must have at least one entry in 'slots'");
            }

            var result = new ComboFile(
                name,
                ReadString(root, "class"),
                classId,
                ReadString(root, "spec"),
                ReadString(root, "source"),
                ReadString(root, "description"),
                layout,
                entries);
            logger.Info("combo.loaded", "Combo file loaded", new Dictionary<string, object?>
            {
                ["combo"] = name,
                ["class_id"] = classId,
                ["entries"] = entries.Count,
            });
            return result;
        }
        catch (OperationRefusedException ex)
        {
            scope.Fail(ex);
            throw;
        }
    }

    /// <summary>Parses repeated SLOT=SKILLNO values (SLOT is a 0-based game slot, 0..19).</summary>
    public static IReadOnlyDictionary<int, int> ParseOverrides(IEnumerable<string> values)
    {
        var overrides = new Dictionary<int, int>();
        foreach (var value in values)
        {
            var match = OverrideRe.Match(value.Trim());
            if (!match.Success)
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"invalid skill override '{value}'; expected SLOT=SKILLNO");
            }

            var slot = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var skillNo = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (slot >= SlotCount)
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"skill override '{value}': SLOT must be 0..{SlotCount - 1}");
            }

            if (skillNo <= 0)
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"skill override '{value}': SKILLNO must be positive");
            }

            if (overrides.ContainsKey(slot))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"skill override given twice for slot {slot}");
            }

            overrides[slot] = skillNo;
        }

        return overrides;
    }

    /// <summary>
    /// The combo as cooldown slots, in the port of the Python tool's build_plan: overrides replace the SkillNo of a slot
    /// (the memo stays with the entry, so it follows the new SkillNo), optional entries can be skipped (the later
    /// entries of their group move up). An override on a slot the combo does not use is refused.
    /// </summary>
    public static IReadOnlyList<CooldownSlot> BuildCooldownSlots(
        ComboFile combo,
        IReadOnlyDictionary<int, int> overrides,
        bool skipOptional,
        AppLogger logger)
    {
        var entrySlots = combo.Entries.Select(e => e.Slot).ToHashSet();
        foreach (var slot in overrides.Keys)
        {
            if (!entrySlots.Contains(slot))
            {
                throw new OperationRefusedException(
                    OperationProblem.InvalidRequest,
                    $"skill override refers to slot {slot}, which the combo does not use");
            }
        }

        var planned = new List<CooldownSlot>();
        foreach (var group in GroupOrder)
        {
            var first = combo.Layout[group].First;
            var position = 0;
            foreach (var entry in combo.Entries.Where(e => e.Group == group))
            {
                var skillNo = overrides.TryGetValue(entry.Slot, out var replacement) ? replacement : entry.SkillNo;
                if (overrides.ContainsKey(entry.Slot))
                {
                    logger.Info("combo.override_applied", "Skill override applied", new Dictionary<string, object?>
                    {
                        ["slot"] = entry.Slot,
                        ["from_skill"] = entry.SkillNo,
                        ["to_skill"] = skillNo,
                    });
                }

                if (skipOptional && entry.Optional)
                {
                    logger.Info("combo.optional_skipped", "Skipped optional skill", new Dictionary<string, object?>
                    {
                        ["slot"] = entry.Slot,
                        ["group"] = group,
                    });
                    continue;
                }

                planned.Add(new CooldownSlot(first + position, skillNo, entry.SkillName, entry.Memo, entry.GuideName));
                position++;
            }
        }

        return planned;
    }

    private static JsonDocument ParseDocument(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new OperationRefusedException(OperationProblem.InvalidRequest, $"cannot read combo file {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static OperationRefusedException Invalid(string message) => new(OperationProblem.InvalidRequest, message);

    private static bool TryInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;

    private static Dictionary<string, (int First, int Last)> ParseLayout(JsonElement root)
    {
        if (!root.TryGetProperty("layout", out var layout) || layout.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("combo must have a 'layout' object");
        }

        var keys = layout.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (!keys.SetEquals(GroupOrder))
        {
            throw Invalid($"layout must have exactly the keys {string.Join(", ", GroupOrder)}");
        }

        var ranges = new Dictionary<string, (int First, int Last)>(StringComparer.Ordinal);
        foreach (var group in GroupOrder)
        {
            var element = layout.GetProperty(group);
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2
                || !TryInt(element[0], out var first) || !TryInt(element[1], out var last))
            {
                throw Invalid($"layout.{group} must be a [first, last] pair of integers");
            }

            if (!(0 <= first && first <= last && last < SlotCount))
            {
                throw Invalid($"layout.{group} must satisfy 0 <= first <= last <= {SlotCount - 1}");
            }

            ranges[group] = (first, last);
        }

        var sorted = ranges.Select(kv => (kv.Value.First, kv.Value.Last, Key: kv.Key)).OrderBy(r => r.First).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            var previous = sorted[i - 1];
            var current = sorted[i];
            if (current.First <= previous.Last)
            {
                throw Invalid($"layout ranges overlap: {previous.Key} {previous.First}-{previous.Last} and {current.Key} {current.First}-{current.Last}");
            }
        }

        return ranges;
    }

    private static List<ComboEntry> ParseEntries(JsonElement root, string group, Dictionary<string, (int First, int Last)> layout)
    {
        var arrayName = GroupArrays[group];
        if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"'{arrayName}' must be an array");
        }

        var (first, last) = layout[group];
        var capacity = last - first + 1;
        if (array.GetArrayLength() > capacity)
        {
            throw Invalid($"'{arrayName}' has {array.GetArrayLength()} entries but its layout range {first}-{last} holds {capacity}");
        }

        var entries = new List<ComboEntry>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var where = $"{arrayName}[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"{where} must be an object");
            }

            if (item.TryGetProperty("slot", out _))
            {
                throw Invalid($"{where} must not have 'slot': slot numbers come from the layout");
            }

            if (!item.TryGetProperty("skillNo", out var skillElement) || !TryInt(skillElement, out var skillNo) || skillNo <= 0)
            {
                throw Invalid($"{where}: skillNo must be a positive integer");
            }

            string? memo = null;
            if (item.TryGetProperty("memo", out var memoElement) && memoElement.ValueKind != JsonValueKind.Null)
            {
                if (memoElement.ValueKind != JsonValueKind.String)
                {
                    throw Invalid($"{where}: memo must be a string or null");
                }

                memo = memoElement.GetString() ?? string.Empty;
                if (memo.Contains('\n') || memo.Contains('\r'))
                {
                    throw Invalid($"{where}: memo must be a single line");
                }

                if (memo.Length == 0)
                {
                    memo = null; // an empty memo creates no memo entry
                }
            }

            var optional = false;
            if (item.TryGetProperty("optional", out var optionalElement))
            {
                if (optionalElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw Invalid($"{where}: optional must be true or false");
                }

                optional = optionalElement.ValueKind == JsonValueKind.True;
            }

            entries.Add(new ComboEntry(
                group,
                first + index,
                ReadString(item, "skill"),
                ReadString(item, "skillName"),
                skillNo,
                memo,
                optional));
            index++;
        }

        return entries;
    }
}
