using System.Globalization;
using System.Text.RegularExpressions;

namespace BdoComboSetup.Core.GameData;

/// <summary>A quick slot write that the format does not allow (unknown record, missing section, unsupported version).</summary>
public sealed class QuickSlotRefusedException(string message) : Exception(message)
{
}

public enum QuickRecordKind
{
    /// <summary>QuickEmpty: no skill in the position.</summary>
    Empty,

    /// <summary>QuickSlotSkillData: a skill (SkillNo) in the position.</summary>
    Skill,

    /// <summary>QuickSlotItemData: an item in the position. Never written by this code.</summary>
    Item,

    /// <summary>Any other tag. Never written by this code.</summary>
    Other,
}

/// <summary>One attribute as written in the file. Value is the raw text between the quotes (XML-escaped).</summary>
public sealed record QuickAttribute(string Name, string Value);

/// <summary>
/// One record line of the QuickSlotData section. <see cref="LineIndex"/> is its line in the section body,
/// <see cref="RawLine"/> the whole line as stored (including the CR when the file uses CRLF).
/// </summary>
public sealed record QuickRecord(
    int Key,
    string TagName,
    QuickRecordKind Kind,
    int? SkillNo,
    IReadOnlyList<QuickAttribute> Attributes,
    string RawLine,
    int LineIndex,
    string Indent,
    string LineEnding);

/// <summary>Put skill <paramref name="SkillNo"/> into quick bar position 1..9, or clear the position when null.</summary>
public sealed record QuickSlotAssignment(int Position, int? SkillNo);

public sealed record QuickSlotChange(int Position, int? OldSkillNo, int? NewSkillNo);

/// <summary>
/// Reads and writes the &lt;QuickSlotData Version="3"&gt; section of gameVariable.xml as text. Position N (1..9) is record
/// Key N-1. Records are edited line by line; every line that is not assigned keeps its exact bytes.
/// See app/docs/quickslotdata-format.md for the format and its evidence.
/// </summary>
public static class QuickSlotData
{
    public const int RecordCount = 20;
    public const int PositionCount = 9;
    public const string RequiredVersion = "3";

    private const string Placeholder = "\0QUICK\0";

    private static readonly Regex OpenRe = new(
        "<QuickSlotData Version=\"([0-9]+)\">(.*?)</QuickSlotData>", RegexOptions.Singleline);
    private static readonly Regex SelfClosedRe = new("<QuickSlotData Version=\"([0-9]+)\"\\s*/>");
    private static readonly Regex AnyTagRe = new("<QuickSlotData[\\s/>]");
    private static readonly Regex RecordRe = new("\\A([ \\t]*)<([A-Za-z]+)((?:\\s+[A-Za-z]+=\"[^\"]*\")*)\\s*/>\\z");
    private static readonly Regex AttributeRe = new("([A-Za-z]+)=\"([^\"]*)\"");
    private static readonly Regex DigitsRe = new("\\A[0-9]+\\z");

    private sealed record Section(int Start, int End, int BodyStart, int BodyEnd, string Version, bool SelfClosed);

    /// <summary>The records of the section. A self-closed (empty) section gives no records.</summary>
    /// <exception cref="SaveFormatException">The section is absent or does not have the expected layout.</exception>
    public static IReadOnlyList<QuickRecord> Read(string text)
    {
        var section = Locate(text);
        return section.SelfClosed ? [] : ParseRecords(text[section.BodyStart..section.BodyEnd]);
    }

    /// <summary>
    /// The file with the QuickSlotData section replaced by a placeholder, so that the rest of the file can be compared.
    /// Returns the text unchanged when the file has no such section.
    /// </summary>
    public static string Mask(string text)
    {
        if (!AnyTagRe.IsMatch(text))
        {
            return text;
        }

        var section = Locate(text);
        return text[..section.Start] + Placeholder + text[section.End..];
    }

    /// <summary>
    /// Returns <paramref name="text"/> with the assigned positions written. Positions not assigned keep their records.
    /// Throws <see cref="QuickSlotRefusedException"/> when the format cannot be written safely (missing or empty section,
    /// unsupported version, or an item or unknown record in a target position).
    /// </summary>
    public static string Apply(string text, IReadOnlyList<QuickSlotAssignment> assignments)
    {
        if (assignments.Count == 0)
        {
            return text;
        }

        ValidateAssignments(assignments);
        var section = Locate(text);
        if (section.SelfClosed)
        {
            throw new QuickSlotRefusedException(
                "QuickSlotData has no records in this file; place a skill in the game's quick bar first");
        }

        if (section.Version != RequiredVersion)
        {
            throw new QuickSlotRefusedException(
                $"QuickSlotData Version {section.Version} is not supported (only Version {RequiredVersion})");
        }

        var body = text[section.BodyStart..section.BodyEnd];
        var records = ParseRecords(body);
        var lines = body.Split('\n');
        foreach (var assignment in assignments)
        {
            var record = records[assignment.Position - 1];
            if (record.Kind is QuickRecordKind.Item or QuickRecordKind.Other)
            {
                throw new QuickSlotRefusedException(
                    $"quick slot {assignment.Position} holds a {record.TagName} record; clear it in the game first " +
                    "(only skill and empty records are written)");
            }

            var replacement = Replacement(record, assignment.SkillNo);
            if (replacement is not null)
            {
                lines[record.LineIndex] = replacement;
            }
        }

        return text[..section.BodyStart] + string.Join('\n', lines) + text[section.BodyEnd..];
    }

    /// <summary>The positions whose skill would change when the assignments are applied to <paramref name="text"/>.</summary>
    public static IReadOnlyList<QuickSlotChange> Diff(string text, IReadOnlyList<QuickSlotAssignment> assignments)
    {
        var records = Read(text);
        var changes = new List<QuickSlotChange>();
        foreach (var assignment in assignments)
        {
            int? oldSkill = null;
            if (records.Count == RecordCount)
            {
                var record = records[assignment.Position - 1];
                oldSkill = record.Kind == QuickRecordKind.Skill ? record.SkillNo : null;
            }

            if (oldSkill != assignment.SkillNo)
            {
                changes.Add(new QuickSlotChange(assignment.Position, oldSkill, assignment.SkillNo));
            }
        }

        return changes;
    }

    /// <summary>
    /// Checks a written quick slot section against the text before writing. Returns the problems (empty means OK).
    /// Every record that was not assigned must keep its exact line.
    /// </summary>
    public static IReadOnlyList<string> Verify(string beforeText, string afterText, IReadOnlyList<QuickSlotAssignment> assignments)
    {
        var problems = new List<string>();
        IReadOnlyList<QuickRecord> before;
        IReadOnlyList<QuickRecord> after;
        try
        {
            before = Read(beforeText);
            after = Read(afterText);
        }
        catch (SaveFormatException ex)
        {
            return [$"quick slot data does not parse: {ex.Message}"];
        }

        var expected = assignments.ToDictionary(a => a.Position, a => a.SkillNo);
        for (var index = 0; index < RecordCount; index++)
        {
            var position = index + 1;
            var record = after[index];
            if (expected.TryGetValue(position, out var skillNo))
            {
                var ok = skillNo is null
                    ? record.Kind == QuickRecordKind.Empty
                    : record.Kind == QuickRecordKind.Skill && record.SkillNo == skillNo;
                if (!ok)
                {
                    problems.Add($"quick slot {position} does not hold the expected record");
                }
            }
            else if (before[index].RawLine != record.RawLine)
            {
                problems.Add($"quick slot record {position} changed although it was not assigned");
            }
        }

        if (Mask(beforeText) != Mask(afterText))
        {
            problems.Add("bytes outside the QuickSlotData section changed");
        }

        return problems;
    }

    private static void ValidateAssignments(IReadOnlyList<QuickSlotAssignment> assignments)
    {
        var positions = new HashSet<int>();
        foreach (var assignment in assignments)
        {
            if (assignment.Position is < 1 or > PositionCount)
            {
                throw new QuickSlotRefusedException($"quick slot position {assignment.Position} is outside 1..{PositionCount}");
            }

            if (!positions.Add(assignment.Position))
            {
                throw new QuickSlotRefusedException($"quick slot position {assignment.Position} is assigned twice");
            }

            if (assignment.SkillNo is <= 0)
            {
                throw new QuickSlotRefusedException("quick slot skill numbers must be positive, or null to clear the position");
            }
        }
    }

    private static string? Replacement(QuickRecord record, int? skillNo)
    {
        if (skillNo is null)
        {
            return record.Kind == QuickRecordKind.Skill
                ? Render(record.Indent, "QuickEmpty", ToEmptyAttributes(record.Attributes), record.LineEnding)
                : null;
        }

        if (record.Kind == QuickRecordKind.Empty)
        {
            return Render(record.Indent, "QuickSlotSkillData", ToSkillAttributes(record.Attributes, skillNo.Value), record.LineEnding);
        }

        if (record.Kind == QuickRecordKind.Skill && record.SkillNo != skillNo)
        {
            var changed = record.Attributes
                .Select(a => a.Name == "SkillNo" ? a with { Value = skillNo.Value.ToString(CultureInfo.InvariantCulture) } : a)
                .ToList();
            return Render(record.Indent, record.TagName, changed, record.LineEnding);
        }

        return null;
    }

    /// <summary>A cleared skill record: QuickEmpty without SkillNo and SkillLevel, KeyboardKey 0 as empty records have it.</summary>
    private static List<QuickAttribute> ToEmptyAttributes(IReadOnlyList<QuickAttribute> attributes)
    {
        var result = attributes
            .Where(a => a.Name is not ("SkillNo" or "SkillLevel"))
            .Select(a => a.Name == "KeyboardKey" ? a with { Value = "0" } : a)
            .ToList();
        return result;
    }

    /// <summary>A placed skill: QuickSlotSkillData with SkillNo and SkillLevel 1 after Key; other attributes kept as found.</summary>
    private static List<QuickAttribute> ToSkillAttributes(IReadOnlyList<QuickAttribute> attributes, int skillNo)
    {
        var result = new List<QuickAttribute>();
        foreach (var attribute in attributes)
        {
            result.Add(attribute);
            if (attribute.Name == "Key")
            {
                result.Add(new QuickAttribute("SkillNo", skillNo.ToString(CultureInfo.InvariantCulture)));
                result.Add(new QuickAttribute("SkillLevel", "1"));
            }
        }

        return result;
    }

    private static string Render(string indent, string tag, IEnumerable<QuickAttribute> attributes, string lineEnding) =>
        indent + "<" + tag + string.Concat(attributes.Select(a => $" {a.Name}=\"{a.Value}\"")) + "/>" + lineEnding;

    private static Section Locate(string text)
    {
        var tags = AnyTagRe.Matches(text).Count;
        if (tags == 0)
        {
            throw new SaveFormatException("QuickSlotData section not found");
        }

        if (tags > 1)
        {
            throw new SaveFormatException("expected at most one QuickSlotData section");
        }

        var open = OpenRe.Match(text);
        if (open.Success)
        {
            var body = open.Groups[2];
            return new Section(open.Index, open.Index + open.Length, body.Index, body.Index + body.Length, open.Groups[1].Value, false);
        }

        var selfClosed = SelfClosedRe.Match(text);
        if (selfClosed.Success)
        {
            var end = selfClosed.Index + selfClosed.Length;
            return new Section(selfClosed.Index, end, end, end, selfClosed.Groups[1].Value, true);
        }

        throw new SaveFormatException("QuickSlotData section is malformed");
    }

    private static List<QuickRecord> ParseRecords(string body)
    {
        var lines = body.Split('\n');
        var records = new List<QuickRecord>();
        for (var index = 0; index < lines.Length; index++)
        {
            var raw = lines[index];
            var ending = raw.EndsWith('\r') ? "\r" : string.Empty;
            var content = ending.Length == 0 ? raw : raw[..^1];
            if (content.Trim().Length == 0)
            {
                continue; // blank lines are kept as they are
            }

            var match = RecordRe.Match(content);
            if (!match.Success)
            {
                throw new SaveFormatException("unexpected line inside QuickSlotData");
            }

            var tag = match.Groups[2].Value;
            var attributes = AttributeRe.Matches(match.Groups[3].Value)
                .Select(m => new QuickAttribute(m.Groups[1].Value, m.Groups[2].Value))
                .ToList();

            var keyText = attributes.FirstOrDefault(a => a.Name == "Key")?.Value;
            if (keyText is null || !DigitsRe.IsMatch(keyText))
            {
                throw new SaveFormatException($"{tag} record without a numeric Key");
            }

            var kind = tag switch
            {
                "QuickEmpty" => QuickRecordKind.Empty,
                "QuickSlotSkillData" => QuickRecordKind.Skill,
                "QuickSlotItemData" => QuickRecordKind.Item,
                _ => QuickRecordKind.Other,
            };

            int? skillNo = null;
            if (kind == QuickRecordKind.Skill)
            {
                var skillText = attributes.FirstOrDefault(a => a.Name == "SkillNo")?.Value;
                if (skillText is null || !DigitsRe.IsMatch(skillText))
                {
                    throw new SaveFormatException("QuickSlotSkillData record without a numeric SkillNo");
                }

                skillNo = int.Parse(skillText, CultureInfo.InvariantCulture);
            }

            records.Add(new QuickRecord(
                int.Parse(keyText, CultureInfo.InvariantCulture),
                tag,
                kind,
                skillNo,
                attributes,
                raw,
                index,
                match.Groups[1].Value,
                ending));
        }

        if (records.Count != RecordCount || records.Where((record, i) => record.Key != i).Any())
        {
            throw new SaveFormatException(
                $"QuickSlotData must hold exactly {RecordCount} records with Key 0..{RecordCount - 1} in order");
        }

        return records;
    }
}
