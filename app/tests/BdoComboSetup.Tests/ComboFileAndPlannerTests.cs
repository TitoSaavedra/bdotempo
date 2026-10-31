using System.Text;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Tests;

public sealed class ComboFileAndPlannerTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private string WriteCombo(string json, string name = "tamer.awakening")
    {
        var path = Path.Combine(_log.Directory, name + ".json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private const string ValidCombo = """
        {
          "schema": 2,
          "class": "tamer",
          "classId": 16,
          "spec": "awakening",
          "source": "test",
          "description": "test combo",
          "layout": {"combo": [0, 5], "priorityBuffs": [12, 14], "otherBuffs": [15, 19]},
          "slots": [
            {"skill": "Flow: Intimidation", "skillName": "Flujo: Intimidación", "skillNo": 1937, "memo": "R 1"},
            {"skill": "Allround Spinner", "skillName": "Aguja giratoria completa", "skillNo": 1921, "memo": "SH + DER"},
            {"skill": "Dark Gaoler", "skillName": "Guardiacárcel Negro", "skillNo": 1306, "memo": "F", "optional": true}
          ],
          "priorityBuffs": [
            {"skill": "Summon Heuklang", "skillName": "Invocar a Heuklang", "skillNo": 1347, "memo": ""},
            {"skill": "Absorb Heuklang", "skillName": "Absorción de Heuklang", "skillNo": 231, "memo": "E"}
          ],
          "otherBuffs": []
        }
        """;

    private static void AssertRefused(Action action, string contains)
    {
        var error = Assert.Throws<OperationRefusedException>(action);
        Assert.Equal(OperationProblem.InvalidRequest, error.Problem);
        Assert.Contains(contains, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_combo_file_is_read_with_its_layout_and_entries()
    {
        var combo = ComboFileReader.Read(WriteCombo(ValidCombo), _log.Logger);

        Assert.Equal("tamer.awakening", combo.Name);
        Assert.Equal(16, combo.ClassId);
        Assert.Equal(3, combo.Entries.Count(e => e.Group == "combo"));
        Assert.Equal((12, 14), combo.Layout["priorityBuffs"]);
        var first = combo.Entries[0];
        Assert.Equal(0, first.Slot);
        Assert.Equal(1937, first.SkillNo);
        Assert.Equal("Flujo: Intimidación", first.SkillName);
        Assert.Equal("R 1", first.Memo);
        Assert.Null(combo.Entries.Single(e => e.SkillNo == 1347).Memo);
        Assert.True(combo.Entries.Single(e => e.SkillNo == 1306).Optional);
        Assert.Empty(combo.Warnings);
    }

    [Fact]
    public void A_wrong_schema_is_refused()
    {
        AssertRefused(() => ComboFileReader.Read(WriteCombo(ValidCombo.Replace("\"schema\": 2", "\"schema\": 1")), _log.Logger), "unsupported combo schema");
    }

    [Fact]
    public void Overlapping_layout_ranges_are_refused()
    {
        var json = ValidCombo.Replace("\"priorityBuffs\": [12, 14]", "\"priorityBuffs\": [5, 14]");
        AssertRefused(() => ComboFileReader.Read(WriteCombo(json), _log.Logger), "overlap");
    }

    [Fact]
    public void A_layout_outside_0_to_19_is_refused()
    {
        var json = ValidCombo.Replace("\"combo\": [0, 5]", "\"combo\": [0, 20]");
        AssertRefused(() => ComboFileReader.Read(WriteCombo(json), _log.Logger), "0 <= first <= last <= 19");
    }

    [Fact]
    public void An_array_longer_than_its_range_is_refused()
    {
        var json = ValidCombo.Replace("\"combo\": [0, 5]", "\"combo\": [0, 1]");
        AssertRefused(() => ComboFileReader.Read(WriteCombo(json), _log.Logger), "holds 2");
    }

    [Fact]
    public void An_entry_with_a_slot_key_is_refused()
    {
        var json = ValidCombo.Replace("\"skillNo\": 1937,", "\"skillNo\": 1937, \"slot\": 0,");
        AssertRefused(() => ComboFileReader.Read(WriteCombo(json), _log.Logger), "must not have 'slot'");
    }

    [Fact]
    public void A_multi_line_memo_is_refused()
    {
        var json = ValidCombo.Replace("\"memo\": \"R 1\"", "\"memo\": \"R\\n1\"");
        AssertRefused(() => ComboFileReader.Read(WriteCombo(json), _log.Logger), "single line");
    }

    [Fact]
    public void A_combo_without_slots_is_refused()
    {
        var json = ValidCombo.Replace("\"slots\": [", "\"slots\": [] , \"unused\": [");
        AssertRefused(() => ComboFileReader.Read(WriteCombo(json), _log.Logger), "at least one entry");
    }

    [Fact]
    public void A_duplicate_skill_is_a_warning_not_an_error()
    {
        var json = ValidCombo.Replace("\"skillNo\": 1921", "\"skillNo\": 1937");
        var combo = ComboFileReader.Read(WriteCombo(json), _log.Logger);

        Assert.Single(combo.Warnings);
        Assert.Contains("same skillNo", combo.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Skip_optional_moves_later_entries_of_the_group_up()
    {
        var combo = ComboFileReader.Read(WriteCombo(ValidCombo.Replace("\"skillNo\": 1306", "\"skillNo\": 1306").Replace(
            "{\"skill\": \"Dark Gaoler\"", "{\"skill\": \"Dark Gaoler\"")), _log.Logger);
        var json = ValidCombo.Replace(
            "{\"skill\": \"Dark Gaoler\", \"skillName\": \"Guardiacárcel Negro\", \"skillNo\": 1306, \"memo\": \"F\", \"optional\": true}",
            "{\"skill\": \"Dark Gaoler\", \"skillName\": \"Guardiacárcel Negro\", \"skillNo\": 1306, \"memo\": \"F\", \"optional\": true},"
            + "{\"skill\": \"Garuda\", \"skillName\": \"Garuda\", \"skillNo\": 1912, \"memo\": \"F\"}");
        combo = ComboFileReader.Read(WriteCombo(json, "skip"), _log.Logger);

        var kept = ComboFileReader.BuildCooldownSlots(combo, new Dictionary<int, int>(), skipOptional: false, _log.Logger);
        var skipped = ComboFileReader.BuildCooldownSlots(combo, new Dictionary<int, int>(), skipOptional: true, _log.Logger);

        Assert.Equal([0, 1, 2, 3, 12, 13], kept.Select(s => s.Slot).ToArray());
        Assert.Equal([0, 1, 2], skipped.Where(s => s.SkillNo != 1347 && s.SkillNo != 231).Select(s => s.Slot).ToArray());
        Assert.DoesNotContain(skipped, s => s.SkillNo == 1306);
        Assert.Equal(1912, skipped.Single(s => s.Slot == 2).SkillNo);
    }

    [Fact]
    public void An_override_replaces_the_skill_of_its_slot_and_the_memo_follows()
    {
        var combo = ComboFileReader.Read(WriteCombo(ValidCombo), _log.Logger);
        var overrides = ComboFileReader.ParseOverrides(["1=1918"]);

        var slots = ComboFileReader.BuildCooldownSlots(combo, overrides, skipOptional: false, _log.Logger);

        var slot = slots.Single(s => s.Slot == 1);
        Assert.Equal(1918, slot.SkillNo);
        Assert.Equal("SH + DER", slot.Memo);
        Assert.Equal("Aguja giratoria completa", slot.SkillName);
    }

    [Fact]
    public void An_override_on_a_slot_the_combo_does_not_use_is_refused()
    {
        var combo = ComboFileReader.Read(WriteCombo(ValidCombo), _log.Logger);

        AssertRefused(() => ComboFileReader.BuildCooldownSlots(combo, ComboFileReader.ParseOverrides(["14=1921"]), false, _log.Logger), "does not use");
    }

    [Theory]
    [InlineData("0", "invalid skill override")]
    [InlineData("x=1", "invalid skill override")]
    [InlineData("20=1921", "SLOT must be 0..19")]
    [InlineData("0=0", "SKILLNO must be positive")]
    public void Bad_override_text_is_refused(string value, string message)
    {
        AssertRefused(() => ComboFileReader.ParseOverrides([value]), message);
    }

    [Fact]
    public void The_same_override_twice_is_refused()
    {
        AssertRefused(() => ComboFileReader.ParseOverrides(["0=1", "0=2"]), "twice");
    }

    [Fact]
    public void The_plan_keeps_the_first_memo_of_a_skill_and_warns_about_conflicts()
    {
        var warnings = new List<string>();
        var slots = new[]
        {
            new CooldownSlot(0, 1926, "Baile de la Bestia Legendaria", "A + DER"),
            new CooldownSlot(8, 1926, "Baile de la Bestia Legendaria", "B"),
            new CooldownSlot(9, 1937, "Flujo: Intimidación", null),
        };

        var plan = CooldownPlanner.Build(slots, _log.Logger, warnings);

        Assert.Equal("A + DER", plan.Memos[1926]);
        Assert.False(plan.Memos.ContainsKey(1937));
        Assert.Contains(warnings, w => w.Contains("conflicting memos", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("same skillNo", StringComparison.Ordinal));
        Assert.Equal(1926, plan.Skills[0]);
        Assert.Equal(0, plan.Skills[1]);
    }

    [Fact]
    public void The_plan_refuses_bad_slots()
    {
        var warnings = new List<string>();

        Assert.Throws<OperationRefusedException>(() => CooldownPlanner.Build([new CooldownSlot(20, 1937, "x", null)], _log.Logger, warnings));
        Assert.Throws<OperationRefusedException>(() => CooldownPlanner.Build([new CooldownSlot(1, 1937, "x", null), new CooldownSlot(1, 1921, "y", null)], _log.Logger, warnings));
        Assert.Throws<OperationRefusedException>(() => CooldownPlanner.Build([new CooldownSlot(1, 0, "x", null)], _log.Logger, warnings));
        Assert.Throws<OperationRefusedException>(() => CooldownPlanner.Build([new CooldownSlot(1, 1937, "x", "a\nb")], _log.Logger, warnings));
    }
}
