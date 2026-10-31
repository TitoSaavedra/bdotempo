using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.GameData;

/// <summary>One skill rank of the skill table (class_skills.json, produced by the Scrappers project).</summary>
public sealed record SkillInfo(int SkillNo, IReadOnlySet<int> ClassIds, string GroupName, string DisplayName);

/// <summary>A combo slot's skill, as needed for cross-checking against the table.</summary>
public sealed record SkillRef(int Slot, string GuideName, string SkillName, int SkillNo);

public sealed record SkillFinding(int Slot, int SkillNo, string TableName, IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>Skill table access and name checks (port of the Python tool's skills.py).</summary>
public sealed class SkillTable
{
    private static readonly Regex MarkupRe = new("<[^>]+>");
    private static readonly Regex SpacesRe = new("\\s+");
    private static readonly Regex RomanSuffixRe = new("\\s+[IVXLCDM]+$");

    private readonly Dictionary<int, SkillInfo> _bySkillNo;

    private SkillTable(Dictionary<int, SkillInfo> bySkillNo)
    {
        _bySkillNo = bySkillNo;
    }

    public int Count => _bySkillNo.Count;

    /// <summary>Strips game markup such as &lt;PAColor0xff2bbbff&gt; and collapses whitespace.</summary>
    public static string CleanName(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return SpacesRe.Replace(MarkupRe.Replace(text, string.Empty), " ").Trim();
    }

    /// <summary>"Garuda I" becomes "Garuda" (trailing roman numeral removed).</summary>
    public static string StripRankSuffix(string name) => RomanSuffixRe.Replace(CleanName(name), string.Empty);

    /// <summary>Case-insensitive prefix comparison after removing markup and roman numerals.</summary>
    public static bool NamesMatch(string expected, string groupName)
    {
        var want = StripRankSuffix(expected).ToLowerInvariant();
        var have = StripRankSuffix(groupName).ToLowerInvariant();
        if (want.Length == 0 || have.Length == 0)
        {
            return false;
        }

        return have.StartsWith(want, StringComparison.Ordinal) || want.StartsWith(have, StringComparison.Ordinal);
    }

    /// <summary>Loads the table. A missing or unreadable file is refused (the apply is never attempted without it).</summary>
    public static SkillTable Load(string path, AppLogger logger)
    {
        using var scope = logger.BeginOperation("skills.load", new Dictionary<string, object?> { ["file"] = Path.GetFileName(path) });
        try
        {
            if (!File.Exists(path))
            {
                throw new OperationRefusedException(OperationProblem.SkillTableMissing, $"skills file not found: {Path.GetFileName(path)}");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            {
                throw new OperationRefusedException(OperationProblem.SkillTableMissing, "skills file has no 'groups' list");
            }

            var bySkillNo = new Dictionary<int, SkillInfo>();
            foreach (var group in groups.EnumerateArray())
            {
                var groupName = CleanName(group.TryGetProperty("name", out var name) ? name.GetString() : null);
                var classes = new HashSet<int>();
                if (group.TryGetProperty("classes", out var classArray))
                {
                    foreach (var element in classArray.EnumerateArray())
                    {
                        classes.Add(element.GetInt32());
                    }
                }

                if (!group.TryGetProperty("ranks", out var ranks) || ranks.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var rank in ranks.EnumerateArray())
                {
                    if (!rank.TryGetProperty("skillNo", out var skillElement) || skillElement.ValueKind != JsonValueKind.Number || !skillElement.TryGetInt32(out var skillNo))
                    {
                        logger.Debug("skills.rank_skipped", "Skipped a rank without an integer skillNo", new Dictionary<string, object?> { ["group"] = groupName });
                        continue;
                    }

                    if (bySkillNo.ContainsKey(skillNo))
                    {
                        logger.Debug("skills.duplicate", "Duplicate skillNo in the skills file; keeping the first", new Dictionary<string, object?> { ["skill_no"] = skillNo });
                        continue;
                    }

                    var rankName = rank.TryGetProperty("name", out var rn) ? CleanName(rn.GetString()) : string.Empty;
                    var display = rankName.Length > 0 ? rankName : groupName;
                    bySkillNo[skillNo] = new SkillInfo(skillNo, classes, groupName, display);
                }
            }

            logger.Info("skills.loaded", "Skill table loaded", new Dictionary<string, object?> { ["skills"] = bySkillNo.Count });
            return new SkillTable(bySkillNo);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            throw new OperationRefusedException(OperationProblem.SkillTableMissing, $"cannot read skills file {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    public SkillInfo? Get(int skillNo) => _bySkillNo.GetValueOrDefault(skillNo);

    public string Describe(int skillNo)
    {
        if (skillNo == 0)
        {
            return "(empty)";
        }

        return _bySkillNo.TryGetValue(skillNo, out var info)
            ? info.DisplayName
            : "(unknown skill)";
    }

    /// <summary>Checks every ref: the SkillNo exists, belongs to the class, and its group name matches the expected name.</summary>
    public IReadOnlyList<SkillFinding> Check(IEnumerable<SkillRef> refs, int classId)
    {
        var findings = new List<SkillFinding>();
        foreach (var reference in refs)
        {
            var info = Get(reference.SkillNo);
            if (info is null)
            {
                findings.Add(new SkillFinding(reference.Slot, reference.SkillNo, "(unknown skill)", ["skillNo not found in the skill table"]));
                continue;
            }

            var problems = new List<string>();
            if (!info.ClassIds.Contains(classId))
            {
                problems.Add($"skillNo belongs to classes [{string.Join(", ", info.ClassIds.OrderBy(c => c))}], not classId {classId}");
            }

            if (!NamesMatch(reference.SkillName, info.GroupName))
            {
                problems.Add($"table group '{info.GroupName}' does not match skillName '{reference.SkillName}'");
            }

            findings.Add(new SkillFinding(reference.Slot, reference.SkillNo, info.DisplayName, problems));
        }

        return findings;
    }
}
