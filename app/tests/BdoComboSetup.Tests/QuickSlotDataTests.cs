using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Tests;

public sealed class QuickSlotDataTests
{
    private static string Wrapped(string section = "") => SyntheticSave.Text(quickSection: section.Length == 0 ? SyntheticSave.QuickSection() : section);

    [Fact]
    public void Read_gives_20_records_with_their_kinds_and_skill_numbers()
    {
        var records = QuickSlotData.Read(Wrapped());

        Assert.Equal(20, records.Count);
        Assert.Equal(QuickRecordKind.Skill, records[0].Kind);
        Assert.Equal(5153, records[0].SkillNo);
        Assert.Equal(QuickRecordKind.Skill, records[1].Kind);
        Assert.Equal(QuickRecordKind.Empty, records[2].Kind);
        Assert.Null(records[2].SkillNo);
        Assert.Equal(6457, records[6].SkillNo);
        Assert.Equal(Enumerable.Range(0, 20), records.Select(r => r.Key));
    }

    [Fact]
    public void Read_keeps_the_extra_attribute_of_a_record()
    {
        var records = QuickSlotData.Read(Wrapped());

        Assert.Contains(records[1].Attributes, a => a.Name == "Note" && a.Value == "kept");
    }

    [Fact]
    public void Placing_a_skill_in_an_empty_position_writes_a_skill_record_and_keeps_the_rest()
    {
        var original = Wrapped();
        var updated = QuickSlotData.Apply(original, [new QuickSlotAssignment(3, 1926)]);

        var record = QuickSlotData.Read(updated)[2];
        Assert.Equal(QuickRecordKind.Skill, record.Kind);
        Assert.Equal(1926, record.SkillNo);
        Assert.Equal(
            "\t<QuickSlotSkillData Key=\"2\" SkillNo=\"1926\" SkillLevel=\"1\" Position=\"0.000000 0.000000\" KeyboardKey=\"0\" Memo=\"\"/>",
            record.RawLine.TrimEnd('\r'));
        Assert.Empty(QuickSlotData.Verify(original, updated, [new QuickSlotAssignment(3, 1926)]));
        Assert.Equal(QuickSlotData.Mask(original), QuickSlotData.Mask(updated));
    }

    [Fact]
    public void Changing_the_skill_of_a_skill_record_replaces_only_its_number()
    {
        var original = Wrapped();
        var updated = QuickSlotData.Apply(original, [new QuickSlotAssignment(1, 1347)]);

        var before = QuickSlotData.Read(original)[0].RawLine;
        var after = QuickSlotData.Read(updated)[0].RawLine;
        Assert.Equal(before.Replace("SkillNo=\"5153\"", "SkillNo=\"1347\""), after);
        Assert.Equal(1347, QuickSlotData.Read(updated)[0].SkillNo);
    }

    [Fact]
    public void Clearing_a_skill_position_writes_an_empty_record_without_skill_attributes()
    {
        var original = Wrapped();
        var updated = QuickSlotData.Apply(original, [new QuickSlotAssignment(7, null)]);

        var record = QuickSlotData.Read(updated)[6];
        Assert.Equal(QuickRecordKind.Empty, record.Kind);
        Assert.Equal(
            "\t<QuickEmpty Key=\"6\" Position=\"0.000000 0.000000\" KeyboardKey=\"0\"/>",
            record.RawLine.TrimEnd('\r'));
        Assert.DoesNotContain("SkillNo", record.RawLine);
    }

    [Fact]
    public void Writing_then_clearing_a_position_gives_back_the_original_bytes_for_an_empty_record()
    {
        var original = Wrapped();
        var placed = QuickSlotData.Apply(original, [new QuickSlotAssignment(3, 1926)]);
        var cleared = QuickSlotData.Apply(placed, [new QuickSlotAssignment(3, null)]);

        // The original empty record had Memo="" and KeyboardKey="0"; the cleared record is the same line.
        Assert.Equal(QuickSlotData.Read(original)[2].RawLine, QuickSlotData.Read(cleared)[2].RawLine);
        Assert.Equal(QuickSlotData.Mask(original), QuickSlotData.Mask(cleared));
    }

    [Fact]
    public void Unassigned_records_keep_their_exact_lines_and_line_endings()
    {
        var original = Wrapped();
        var updated = QuickSlotData.Apply(original, [new QuickSlotAssignment(1, 1347), new QuickSlotAssignment(9, 1926)]);

        var before = QuickSlotData.Read(original);
        var after = QuickSlotData.Read(updated);
        for (var key = 0; key < 20; key++)
        {
            if (key is 0 or 8)
            {
                continue;
            }

            Assert.Equal(before[key].RawLine, after[key].RawLine);
        }

        Assert.All(after, r => Assert.EndsWith("\r", r.RawLine, StringComparison.Ordinal));
        Assert.Equal(original.Count(c => c == '\n'), updated.Count(c => c == '\n'));
    }

    [Fact]
    public void A_position_holding_an_item_is_refused()
    {
        var text = Wrapped(SyntheticSave.QuickSectionWithItem());

        var error = Assert.Throws<QuickSlotRefusedException>(() => QuickSlotData.Apply(text, [new QuickSlotAssignment(3, 1926)]));
        Assert.Contains("QuickSlotItemData", error.Message);
    }

    [Fact]
    public void An_item_record_in_another_position_does_not_block_the_write()
    {
        var text = Wrapped(SyntheticSave.QuickSectionWithItem());

        var updated = QuickSlotData.Apply(text, [new QuickSlotAssignment(1, 1347)]);

        Assert.Equal(QuickRecordKind.Item, QuickSlotData.Read(updated)[2].Kind);
        Assert.Equal(QuickSlotData.Read(text)[2].RawLine, QuickSlotData.Read(updated)[2].RawLine);
    }

    [Fact]
    public void A_self_closed_section_is_refused_for_writes()
    {
        var text = SyntheticSave.Text(quickSection: "<QuickSlotData Version=\"3\"/>");

        var error = Assert.Throws<QuickSlotRefusedException>(() => QuickSlotData.Apply(text, [new QuickSlotAssignment(1, 1347)]));
        Assert.Contains("no records", error.Message);
    }

    [Fact]
    public void Version_1_is_refused_for_writes()
    {
        var text = SyntheticSave.Text(quickSection: SyntheticSave.QuickSection().Replace("Version=\"3\"", "Version=\"1\"", StringComparison.Ordinal));

        Assert.Throws<QuickSlotRefusedException>(() => QuickSlotData.Apply(text, [new QuickSlotAssignment(1, 1347)]));
    }

    [Fact]
    public void A_missing_section_is_a_format_error()
    {
        Assert.Throws<SaveFormatException>(() => QuickSlotData.Read(SyntheticSave.Text()));
    }

    [Fact]
    public void Records_out_of_key_order_are_refused()
    {
        var section = SyntheticSave.QuickSection().Replace("Key=\"3\"", "Key=\"4\"", StringComparison.Ordinal);

        Assert.Throws<SaveFormatException>(() => QuickSlotData.Read(SyntheticSave.Text(quickSection: section)));
    }

    [Fact]
    public void Positions_outside_1_to_9_and_duplicates_are_refused()
    {
        var text = Wrapped();

        Assert.Throws<QuickSlotRefusedException>(() => QuickSlotData.Apply(text, [new QuickSlotAssignment(10, 1347)]));
        Assert.Throws<QuickSlotRefusedException>(() => QuickSlotData.Apply(text, [new QuickSlotAssignment(1, 1347), new QuickSlotAssignment(1, 1921)]));
    }

    [Fact]
    public void Verify_flags_a_changed_unassigned_record()
    {
        var original = Wrapped();
        var updated = QuickSlotData.Apply(original, [new QuickSlotAssignment(1, 1347)]);
        var tampered = updated.Replace("KeyboardKey=\"50\"", "KeyboardKey=\"51\"", StringComparison.Ordinal);

        var problems = QuickSlotData.Verify(original, tampered, [new QuickSlotAssignment(1, 1347)]);

        Assert.Contains(problems, p => p.Contains("changed although it was not assigned", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_flags_bytes_outside_the_section()
    {
        var original = Wrapped();
        var updated = QuickSlotData.Apply(original, [new QuickSlotAssignment(1, 1347)]);
        var tampered = updated.Replace("Quest Id", "Quest  Id", StringComparison.Ordinal);

        var problems = QuickSlotData.Verify(original, tampered, [new QuickSlotAssignment(1, 1347)]);

        Assert.Contains(problems, p => p.Contains("outside the QuickSlotData section", StringComparison.Ordinal));
    }

    [Fact]
    public void Diff_lists_only_the_positions_that_change()
    {
        var text = Wrapped();

        var changes = QuickSlotData.Diff(text, [new QuickSlotAssignment(1, 5153), new QuickSlotAssignment(3, 1926), new QuickSlotAssignment(7, null)]);

        Assert.Equal(
            new (int Position, int? Old, int? New)[] { (3, null, 1926), (7, 6457, null) },
            changes.Select(c => (c.Position, c.OldSkillNo, c.NewSkillNo)).ToArray());
    }

    [Fact]
    public void Applying_nothing_returns_the_text_unchanged()
    {
        var text = Wrapped();

        Assert.Equal(text, QuickSlotData.Apply(text, []));
    }
}
