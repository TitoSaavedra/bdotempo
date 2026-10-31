using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Tests;

public sealed class GameVarsTextTests
{
    private static readonly int[] ComboSkills = [1937, 1921, 1912, 1926, 17, 1306, 1932];
    private static readonly Dictionary<int, string> ComboMemos = new()
    {
        [1937] = "R 1",
        [1921] = "SH + DER",
        [1912] = "F (mantener)",
        [1926] = "A + DER",
        [17] = "SH + Z",
        [1306] = "F",
    };

    private static (int Start, int End) Bounds(string text, string open, string close)
    {
        var start = text.IndexOf(open, StringComparison.Ordinal);
        var end = text.IndexOf(close, start, StringComparison.Ordinal) + close.Length;
        return (start, end);
    }

    [Fact]
    public void A_result_passes_verification()
    {
        var original = SyntheticSave.Text();
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos);

        Assert.Empty(GameVarsText.Verify(original, result, ComboSkills, ComboMemos));
    }

    [Fact]
    public void Applying_twice_gives_identical_bytes()
    {
        var once = GameVarsText.ApplyCombo(SyntheticSave.Text(), ComboSkills, ComboMemos);
        var twice = GameVarsText.ApplyCombo(once, ComboSkills, ComboMemos);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Only_the_slot_and_memo_sections_change()
    {
        var original = SyntheticSave.Text();
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos);

        var slot = Bounds(original, "<SkillCoolTimeSlot Version", "</SkillCoolTimeSlot>");
        var memo = Bounds(original, "<SkillCoolTimeMemo Version", "</SkillCoolTimeMemo>");
        var newSlot = Bounds(result, "<SkillCoolTimeSlot Version", "</SkillCoolTimeSlot>");
        var newMemo = Bounds(result, "<SkillCoolTimeMemo Version", "</SkillCoolTimeMemo>");

        Assert.Equal(original[..slot.Start], result[..newSlot.Start]);
        Assert.Equal(original[slot.End..memo.Start], result[newSlot.End..newMemo.Start]);
        Assert.Equal(original[memo.End..], result[newMemo.End..]);
    }

    [Fact]
    public void Output_keeps_crlf_and_has_no_bom()
    {
        var result = GameVarsText.ApplyCombo(SyntheticSave.Text(), ComboSkills, ComboMemos);
        var bytes = System.Text.Encoding.UTF8.GetBytes(result);

        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(result.Split('\n').Length - 1, result.Split("\r\n").Length - 1);
    }

    [Fact]
    public void The_slot_section_has_20_entries_padded_with_zeros()
    {
        var parsed = GameVarsText.Parse(GameVarsText.ApplyCombo(SyntheticSave.Text(), ComboSkills, ComboMemos));

        Assert.Equal(20, parsed.Slots.Count);
        Assert.Equal(ComboSkills, parsed.Slots.Take(ComboSkills.Length));
        Assert.All(parsed.Slots.Skip(ComboSkills.Length), s => Assert.Equal(0, s));
    }

    [Fact]
    public void Memos_are_sorted_unique_and_unrelated_entries_are_kept()
    {
        var result = GameVarsText.ApplyCombo(SyntheticSave.Text(), ComboSkills, ComboMemos);
        var numbers = GameVarsText.Parse(result).Memos.Select(m => m.SkillNo).ToList();

        Assert.Equal(numbers.OrderBy(n => n).ToList(), numbers);
        Assert.Equal(numbers.Distinct().Count(), numbers.Count);
        Assert.Contains("<SkillCoolTimeMemo SkillNo=\"5011\" Memo=\"DIR + DER\"/>", result);
        Assert.Contains("<SkillCoolTimeMemo SkillNo=\"5162\" Memo=\"SH + DER \"/>", result);
        Assert.Contains("<SkillCoolTimeMemo SkillNo=\"5200\" Memo=\"A &amp; B &quot;x&quot; &lt;y&gt;\"/>", result);
    }

    [Fact]
    public void Memo_values_are_escaped_and_verified()
    {
        var original = SyntheticSave.Text();
        var memos = new Dictionary<int, string> { [1937] = "R & L \"x\" <y>" };

        var result = GameVarsText.ApplyCombo(original, [1937], memos);

        Assert.Contains("Memo=\"R &amp; L &quot;x&quot; &lt;y&gt;\"", result);
        Assert.Empty(GameVarsText.Verify(original, result, [1937], memos));
        Assert.Equal("R & L \"x\" <y>", GameVarsText.XmlUnescapeAttr(GameVarsText.XmlEscapeAttr("R & L \"x\" <y>")));
    }

    [Fact]
    public void An_existing_memo_for_a_combo_skill_is_replaced_not_duplicated()
    {
        var original = SyntheticSave.Text();
        var result = GameVarsText.ApplyCombo(original, [5124], new Dictionary<int, string> { [5124] = "NEW" });

        Assert.Equal(1, result.Split("<SkillCoolTimeMemo SkillNo=\"5124\"").Length - 1);
        Assert.Contains("<SkillCoolTimeMemo SkillNo=\"5124\" Memo=\"NEW\"/>", result);
        Assert.DoesNotContain("Memo=\"S + F\"", result);
        Assert.Empty(GameVarsText.Verify(original, result, [5124], new Dictionary<int, string> { [5124] = "NEW" }));
    }

    [Fact]
    public void Tampering_with_bytes_outside_the_sections_is_detected()
    {
        var original = SyntheticSave.Text();
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos);
        var tampered = result.Replace("Quest Id", "Quest  Id", StringComparison.Ordinal);

        var problems = GameVarsText.Verify(original, tampered, ComboSkills, ComboMemos);

        Assert.Contains(problems, p => p.Contains("outside the slot and memo sections", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wrong_slot_list_is_detected()
    {
        var original = SyntheticSave.Text();
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos);

        Assert.NotEmpty(GameVarsText.Verify(original, result, ComboSkills[..^1], ComboMemos));
    }

    [Fact]
    public void A_missing_memo_section_is_created_right_after_the_slot_section()
    {
        var original = SyntheticSave.Text("missing");
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos);

        var slotEnd = result.IndexOf("</SkillCoolTimeSlot>", StringComparison.Ordinal) + "</SkillCoolTimeSlot>".Length;
        Assert.StartsWith("\r\n<SkillCoolTimeMemo Version=\"1\">", result[slotEnd..], StringComparison.Ordinal);
        Assert.Empty(GameVarsText.Verify(original, result, ComboSkills, ComboMemos));
    }

    [Fact]
    public void A_self_closed_memo_section_is_expanded()
    {
        var original = SyntheticSave.Text("self_closed");
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos);

        Assert.DoesNotContain("<SkillCoolTimeMemo Version=\"1\"/>", result);
        Assert.Contains("<SkillCoolTimeMemo Version=\"1\">", result);
        Assert.Empty(GameVarsText.Verify(original, result, ComboSkills, ComboMemos));
    }

    [Fact]
    public void Without_memos_and_without_a_section_a_self_closed_section_is_written()
    {
        var original = SyntheticSave.Text("missing");
        var result = GameVarsText.ApplyCombo(original, [1937], new Dictionary<int, string>());

        Assert.Contains("<SkillCoolTimeMemo Version=\"1\"/>", result);
        Assert.Empty(GameVarsText.Verify(original, result, [1937], new Dictionary<int, string>()));
    }

    [Fact]
    public void Turning_memos_off_leaves_the_memo_section_alone()
    {
        var original = SyntheticSave.Text();
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos, writeMemos: false);

        Assert.Equal(GameVarsText.MemoSectionText(original), GameVarsText.MemoSectionText(result));
    }

    [Fact]
    public void A_slot_section_with_the_wrong_count_is_refused()
    {
        var broken = SyntheticSave.Text().Replace("\t<SkillCoolTimeSlot SlotNo=\"19\" SkillNo=\"0\"/>\r\n", string.Empty, StringComparison.Ordinal);

        Assert.Throws<SaveFormatException>(() => GameVarsText.Parse(broken));
    }

    [Fact]
    public void A_missing_slot_section_is_refused()
    {
        var text = SyntheticSave.Text().Replace("SkillCoolTimeSlot", "OtherSection", StringComparison.Ordinal);

        Assert.Throws<SaveFormatException>(() => GameVarsText.ApplyCombo(text, [1], new Dictionary<int, string>()));
    }

    [Fact]
    public void A_slot_section_of_version_1_is_refused()
    {
        var text = SyntheticSave.Text().Replace("SkillCoolTimeSlot Version=\"2\"", "SkillCoolTimeSlot Version=\"1\"", StringComparison.Ordinal);

        var error = Assert.Throws<SaveFormatException>(() => GameVarsText.Parse(text));
        Assert.Contains("Version 1", error.Message);
    }

    [Fact]
    public void More_than_20_slots_are_refused()
    {
        Assert.Throws<SaveFormatException>(() => GameVarsText.ApplyCombo(SyntheticSave.Text(), new int[21], new Dictionary<int, string>()));
    }

    [Fact]
    public void Lf_only_files_keep_lf()
    {
        var original = SyntheticSave.Text(crlf: false);
        var result = GameVarsText.ApplyCombo(original, ComboSkills, ComboMemos);

        Assert.DoesNotContain("\r", result);
        Assert.Empty(GameVarsText.Verify(original, result, ComboSkills, ComboMemos));
    }

    [Fact]
    public void Parse_reads_the_existing_memos()
    {
        var parsed = GameVarsText.Parse(SyntheticSave.Text());

        Assert.Equal(SyntheticSave.MemoEntries.Length, parsed.Memos.Count);
        Assert.Equal(SyntheticSave.SlotSkills, parsed.Slots);
    }
}
