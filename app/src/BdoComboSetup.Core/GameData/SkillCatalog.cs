using System.Globalization;
using System.Text.Json;

namespace BdoComboSetup.Core.GameData;

/// <summary>
/// The skills of each class at their top rank (the generated class-skills.json of the asset bundle), for the slot
/// editor: the user picks a skill by icon and name, and only the rank a character can actually have is offered.
/// Read-only.
/// </summary>
public sealed class SkillCatalog
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SkillCatalogEntry>> _skillsByClass;

    private SkillCatalog(IReadOnlyDictionary<int, IReadOnlyList<SkillCatalogEntry>> skillsByClass)
    {
        _skillsByClass = skillsByClass;
    }

    /// <summary>A catalog with no skills: the picker stays empty.</summary>
    public static SkillCatalog Empty { get; } = new(new Dictionary<int, IReadOnlyList<SkillCatalogEntry>>());

    public int ClassCount => _skillsByClass.Count;

    /// <summary>The skills of a class, already sorted by name; empty when the catalog does not know the class.</summary>
    public IReadOnlyList<SkillCatalogEntry> SkillsOf(int classType) =>
        _skillsByClass.TryGetValue(classType, out var skills) ? skills : [];

    /// <summary>Parses <c>{"classes": {"&lt;classType&gt;": [{"skillNo", "name", "kind"}, ...]}}</c>.</summary>
    /// <exception cref="FormatException">The text is not the expected catalog.</exception>
    public static SkillCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the skill catalog has no 'classes' object");
            }

            var map = new Dictionary<int, IReadOnlyList<SkillCatalogEntry>>();
            foreach (var classEntry in classes.EnumerateObject())
            {
                if (!int.TryParse(classEntry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var classType) || classEntry.Value.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("a class entry is not a number with a skill list");
                }

                var skills = new List<SkillCatalogEntry>();
                foreach (var skill in classEntry.Value.EnumerateArray())
                {
                    if (!skill.TryGetProperty("skillNo", out var number) || !skill.TryGetProperty("name", out var name))
                    {
                        throw new FormatException("a skill entry has no skillNo or name");
                    }

                    skills.Add(new SkillCatalogEntry(number.GetInt32(), name.GetString() ?? string.Empty));
                }

                map[classType] = skills;
            }

            return new SkillCatalog(map);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the skill catalog is not valid JSON: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"the skill catalog has an unexpected shape: {ex.Message}", ex);
        }
    }
}

/// <summary>One skill of the catalog: the number of its top rank and its Spanish name.</summary>
public sealed record SkillCatalogEntry(int SkillNo, string Name);
