using System.Globalization;
using System.Text.Json;

namespace BdoComboSetup.Core.GameData;

/// <summary>
/// Skill number to the classes that can use it (the generated skill_classes.json of the asset bundle). Only skill groups
/// with at most nine classes are in it, so a skill here says something about the class. Read-only.
/// </summary>
public sealed class SkillClassTable
{
    private readonly IReadOnlyDictionary<int, int[]> _classesBySkill;

    private SkillClassTable(IReadOnlyDictionary<int, int[]> classesBySkill)
    {
        _classesBySkill = classesBySkill;
    }

    /// <summary>A table with no skills: every class inference answers "unknown".</summary>
    public static SkillClassTable Empty { get; } = new(new Dictionary<int, int[]>());

    public int Count => _classesBySkill.Count;

    /// <summary>The classes of a skill number, or an empty list when the table does not know it (0 is an empty slot).</summary>
    public IReadOnlyList<int> ClassesOf(int skillNo) =>
        _classesBySkill.TryGetValue(skillNo, out var classes) ? classes : [];

    /// <summary>Skill numbers that only one class can use, in skill number order (used by the demo data).</summary>
    public IReadOnlyList<int> SkillsOnlyFor(int classType, int count) =>
        _classesBySkill
            .Where(entry => entry.Value.Length == 1 && entry.Value[0] == classType)
            .Select(entry => entry.Key)
            .OrderBy(skillNo => skillNo)
            .Take(count)
            .ToList();

    /// <summary>Parses <c>{"maxClassesPerGroup": 9, "skills": {"&lt;skillNo&gt;": [classType, ...]}}</c>.</summary>
    /// <exception cref="FormatException">The text is not the expected table.</exception>
    public static SkillClassTable Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the skill class table has no 'skills' object");
            }

            var map = new Dictionary<int, int[]>();
            foreach (var entry in skills.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var skillNo) || entry.Value.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("a skill entry is not a number with a class list");
                }

                map[skillNo] = entry.Value.EnumerateArray().Select(element => element.GetInt32()).ToArray();
            }

            return new SkillClassTable(map);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the skill class table is not valid JSON: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"the skill class table has an unexpected shape: {ex.Message}", ex);
        }
    }
}
