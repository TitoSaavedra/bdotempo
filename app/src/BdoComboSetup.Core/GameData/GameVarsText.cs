using System.Text.RegularExpressions;

namespace BdoComboSetup.Core.GameData;

/// <summary>A gameVariable.xml does not have the layout this code understands. Nothing is written.</summary>
public sealed class SaveFormatException(string message) : Exception(message)
{
}

public sealed record MemoEntry(int SkillNo, string Memo, string Raw);

/// <summary>The cooldown slot and memo sections of a file. Memo is the attribute value as stored (XML-escaped).</summary>
public sealed record GameVars(string SlotVersion, IReadOnlyList<int> Slots, string? MemoVersion, IReadOnlyList<MemoEntry> Memos);

/// <summary>
/// Surgical text edits of a character's gameVariable.xml (port of the Python tool's gamevars.py).
/// The file is NOT a single-root XML document, so it is never parsed with an XML library and never re-serialized.
/// Only two sections are touched: SkillCoolTimeSlot (20 entries, Version 2 only) and SkillCoolTimeMemo.
/// Everything else must stay byte-identical; <see cref="Verify"/> checks that.
/// </summary>
public static class GameVarsText
{
    public const int SlotCount = 20;
    public const string RequiredSlotVersion = "2";
    public const string DefaultMemoVersion = "1";

    private const string SlotPlaceholder = "\0SLOT\0";
    private const string MemoPlaceholder = "\0MEMO\0";

    private static readonly Regex SlotSectionRe = new(
        "<SkillCoolTimeSlot Version=\"([0-9]+)\">(.*?)</SkillCoolTimeSlot>", RegexOptions.Singleline);
    private static readonly Regex MemoOpenRe = new(
        "<SkillCoolTimeMemo Version=\"([0-9]+)\">(.*?)</SkillCoolTimeMemo>", RegexOptions.Singleline);
    private static readonly Regex MemoEmptyRe = new("<SkillCoolTimeMemo Version=\"([0-9]+)\"\\s*/>");
    private static readonly Regex SlotEntryRe = new("\\A<SkillCoolTimeSlot SlotNo=\"([0-9]+)\" SkillNo=\"([0-9]+)\"/>\\z");
    private static readonly Regex MemoEntryRe = new("\\A<SkillCoolTimeMemo SkillNo=\"([0-9]+)\" Memo=\"([^\"]*)\"/>\\z");

    private sealed record SectionSpan(int Start, int End, string Version, string? Body);

    private sealed record SectionLayout(SectionSpan Slot, SectionSpan? Memo, string Newline);

    public static string XmlEscapeAttr(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    public static string XmlUnescapeAttr(string value) =>
        value.Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);

    /// <summary>Read the slot and memo sections without changing anything.</summary>
    public static GameVars Parse(string text)
    {
        var layout = Locate(text);
        var slots = ParseSlotBody(layout.Slot.Body ?? string.Empty);
        string? memoVersion = null;
        IReadOnlyList<MemoEntry> memos = [];
        if (layout.Memo is not null)
        {
            memoVersion = layout.Memo.Version;
            memos = layout.Memo.Body is null ? [] : ParseMemoBody(layout.Memo.Body);
        }

        return new GameVars(layout.Slot.Version, slots, memoVersion, memos);
    }

    /// <summary>The exact text of the slot section (from its opening tag to its closing tag).</summary>
    public static string SlotSectionText(string text)
    {
        var layout = Locate(text);
        return text[layout.Slot.Start..layout.Slot.End];
    }

    /// <summary>The exact text of the memo section, or null when the section is absent.</summary>
    public static string? MemoSectionText(string text)
    {
        var layout = Locate(text);
        return layout.Memo is null ? null : text[layout.Memo.Start..layout.Memo.End];
    }

    /// <summary>
    /// Returns <paramref name="text"/> with the slot list replaced and the combo's memos written.
    /// <paramref name="skills"/> fills slots 0..N-1; the rest become 0 (empty). <paramref name="memos"/> maps SkillNo to
    /// memo text: existing entries for those SkillNos are replaced, all other entries are kept. The memo section is
    /// sorted by SkillNo, so applying the same input twice yields identical bytes.
    /// </summary>
    public static string ApplyCombo(
        string text,
        IReadOnlyList<int> skills,
        IReadOnlyDictionary<int, string> memos,
        bool writeSlots = true,
        bool writeMemos = true)
    {
        var padded = Pad(skills);
        var layout = Locate(text);
        var current = Parse(text);
        var newline = layout.Newline;
        var edits = new List<(int Start, int End, string Text)>();

        if (writeSlots)
        {
            var slotLines = new List<string> { $"<SkillCoolTimeSlot Version=\"{layout.Slot.Version}\">" };
            for (var i = 0; i < SlotCount; i++)
            {
                slotLines.Add($"\t<SkillCoolTimeSlot SlotNo=\"{i}\" SkillNo=\"{padded[i]}\"/>");
            }

            slotLines.Add("</SkillCoolTimeSlot>");
            edits.Add((layout.Slot.Start, layout.Slot.End, string.Join(newline, slotLines)));
        }

        if (writeMemos)
        {
            var kept = current.Memos.Where(m => !memos.ContainsKey(m.SkillNo));
            var added = memos.Select(kv =>
            {
                var escaped = XmlEscapeAttr(kv.Value);
                return new MemoEntry(kv.Key, escaped, $"<SkillCoolTimeMemo SkillNo=\"{kv.Key}\" Memo=\"{escaped}\"/>");
            });
            var entries = kept.Concat(added).OrderBy(m => m.SkillNo).ToList();

            if (layout.Memo is null)
            {
                // Memo section absent: create it right after the slot section.
                var created = RenderMemoSection(DefaultMemoVersion, entries, newline);
                edits.Add((layout.Slot.End, layout.Slot.End, newline + created));
            }
            else
            {
                edits.Add((layout.Memo.Start, layout.Memo.End, RenderMemoSection(layout.Memo.Version, entries, newline)));
            }
        }

        return Splice(text, edits);
    }

    /// <summary>
    /// Checks a written result against the original. Returns the problems (an empty list means OK).
    /// </summary>
    public static IReadOnlyList<string> Verify(
        string oldText,
        string newText,
        IReadOnlyList<int> expectedSkills,
        IReadOnlyDictionary<int, string> expectedMemos)
    {
        var problems = new List<string>();
        GameVars old;
        GameVars updated;
        try
        {
            old = Parse(oldText);
            updated = Parse(newText);
        }
        catch (SaveFormatException ex)
        {
            return [$"result does not parse: {ex.Message}"];
        }

        if (!updated.Slots.SequenceEqual(Pad(expectedSkills)))
        {
            problems.Add("slot list does not match the expected list");
        }

        var ids = updated.Memos.Select(m => m.SkillNo).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            problems.Add("duplicate SkillNo memo entries");
        }

        var newMemo = new Dictionary<int, string>();
        foreach (var memo in updated.Memos)
        {
            newMemo[memo.SkillNo] = memo.Memo;
        }

        foreach (var (skillNo, value) in expectedMemos)
        {
            if (!newMemo.TryGetValue(skillNo, out var actual) || actual != XmlEscapeAttr(value))
            {
                problems.Add($"memo for SkillNo {skillNo} is missing or different");
            }
        }

        var keptOld = new Dictionary<int, string>();
        foreach (var memo in old.Memos.Where(m => !expectedMemos.ContainsKey(m.SkillNo)))
        {
            keptOld[memo.SkillNo] = memo.Memo;
        }

        foreach (var (skillNo, memo) in keptOld)
        {
            if (!newMemo.TryGetValue(skillNo, out var actual) || actual != memo)
            {
                problems.Add($"unrelated memo for SkillNo {skillNo} was altered");
            }
        }

        if (updated.Memos.Count != keptOld.Count + expectedMemos.Count)
        {
            problems.Add("unexpected number of memo entries");
        }

        if (Skeleton(oldText) != Skeleton(newText))
        {
            problems.Add("bytes outside the slot and memo sections changed");
        }

        var oldCrlfOnly = CountOf(oldText, "\n") == CountOf(oldText, "\r\n");
        if (oldCrlfOnly && CountOf(newText, "\n") != CountOf(newText, "\r\n"))
        {
            problems.Add("line endings are no longer CRLF");
        }

        return problems;
    }

    /// <summary>
    /// The file with both managed sections replaced by placeholders. A missing memo section is represented as if it
    /// had been inserted right after the slot section, so an absent and a freshly created memo section compare equal.
    /// </summary>
    public static string Skeleton(string text)
    {
        var layout = Locate(text);
        var edits = new List<(int Start, int End, string Text)>
        {
            (layout.Slot.Start, layout.Slot.End, SlotPlaceholder),
        };
        if (layout.Memo is null)
        {
            edits.Add((layout.Slot.End, layout.Slot.End, layout.Newline + MemoPlaceholder));
        }
        else
        {
            edits.Add((layout.Memo.Start, layout.Memo.End, MemoPlaceholder));
        }

        return Splice(text, edits);
    }

    private static SectionLayout Locate(string text)
    {
        var slotMatches = SlotSectionRe.Matches(text);
        if (slotMatches.Count != 1)
        {
            throw new SaveFormatException($"expected exactly one SkillCoolTimeSlot section, found {slotMatches.Count}");
        }

        var match = slotMatches[0];
        var slot = new SectionSpan(match.Index, match.Index + match.Length, match.Groups[1].Value, match.Groups[2].Value);
        if (slot.Version != RequiredSlotVersion)
        {
            throw new SaveFormatException(
                $"SkillCoolTimeSlot Version {slot.Version} is not supported (only Version {RequiredSlotVersion} has 20 slots)");
        }

        var memoCandidates = new List<SectionSpan>();
        foreach (Match open in MemoOpenRe.Matches(text))
        {
            memoCandidates.Add(new SectionSpan(open.Index, open.Index + open.Length, open.Groups[1].Value, open.Groups[2].Value));
        }

        foreach (Match empty in MemoEmptyRe.Matches(text))
        {
            memoCandidates.Add(new SectionSpan(empty.Index, empty.Index + empty.Length, empty.Groups[1].Value, null));
        }

        if (memoCandidates.Count > 1)
        {
            throw new SaveFormatException("expected at most one SkillCoolTimeMemo section");
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new SectionLayout(slot, memoCandidates.Count == 1 ? memoCandidates[0] : null, newline);
    }

    private static IEnumerable<string> BodyLines(string body) =>
        body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    private static IReadOnlyList<int> ParseSlotBody(string body)
    {
        var entries = new List<(int SlotNo, int SkillNo)>();
        foreach (var line in BodyLines(body))
        {
            var match = SlotEntryRe.Match(line);
            if (!match.Success)
            {
                throw new SaveFormatException("unexpected line inside SkillCoolTimeSlot");
            }

            entries.Add((int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (!entries.Select(e => e.SlotNo).SequenceEqual(Enumerable.Range(0, SlotCount)))
        {
            throw new SaveFormatException($"SkillCoolTimeSlot must contain exactly SlotNo 0..{SlotCount - 1} in order");
        }

        return entries.Select(e => e.SkillNo).ToList();
    }

    private static IReadOnlyList<MemoEntry> ParseMemoBody(string body)
    {
        var entries = new List<MemoEntry>();
        foreach (var line in BodyLines(body))
        {
            var match = MemoEntryRe.Match(line);
            if (!match.Success)
            {
                throw new SaveFormatException("unexpected line inside SkillCoolTimeMemo");
            }

            entries.Add(new MemoEntry(
                int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups[2].Value,
                line));
        }

        return entries;
    }

    private static int[] Pad(IReadOnlyList<int> skills)
    {
        if (skills.Count > SlotCount)
        {
            throw new SaveFormatException($"a combo can have at most {SlotCount} slots, got {skills.Count}");
        }

        if (skills.Any(s => s < 0))
        {
            throw new SaveFormatException("skill numbers must be >= 0");
        }

        var padded = new int[SlotCount];
        for (var i = 0; i < skills.Count; i++)
        {
            padded[i] = skills[i];
        }

        return padded;
    }

    private static string RenderMemoSection(string version, IReadOnlyList<MemoEntry> entries, string newline)
    {
        if (entries.Count == 0)
        {
            return $"<SkillCoolTimeMemo Version=\"{version}\"/>";
        }

        var lines = new List<string> { $"<SkillCoolTimeMemo Version=\"{version}\">" };
        lines.AddRange(entries.Select(entry => "\t" + entry.Raw));
        lines.Add("</SkillCoolTimeMemo>");
        return string.Join(newline, lines);
    }

    private static string Splice(string text, IEnumerable<(int Start, int End, string Text)> edits)
    {
        var output = text;
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            output = output[..edit.Start] + edit.Text + output[edit.End..];
        }

        return output;
    }

    private static int CountOf(string text, string token) => text.Split(token).Length - 1;
}
