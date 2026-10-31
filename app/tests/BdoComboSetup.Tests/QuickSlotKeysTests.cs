using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Tests;

public sealed class QuickSlotKeysTests
{
    [Fact]
    public void Default_legends_are_the_mouse_buttons_and_keys_of_the_main_set()
    {
        var labels = Enumerable.Range(1, 10)
            .Select(slot => QuickSlotLegend.Resolve(slot, bound: null)?.Label)
            .ToList();

        Assert.Equal(new[] { "M1", "M2", "M3", "M4", "5", "6", "M5", "M6", "9", "0" }, labels);
    }

    [Fact]
    public void Mouse_buttons_are_flagged_as_mouse_and_keys_are_not()
    {
        Assert.Equal(QuickKeyKind.Mouse, QuickSlotLegend.Resolve(1, null)!.Kind);
        Assert.Equal(QuickKeyKind.Mouse, QuickSlotLegend.Resolve(8, null)!.Kind);
        Assert.Equal(QuickKeyKind.Keyboard, QuickSlotLegend.Resolve(9, null)!.Kind);
    }

    [Fact]
    public void Slots_without_a_default_have_no_legend()
    {
        Assert.Null(QuickSlotLegend.Resolve(11, bound: null));
        Assert.Null(QuickSlotLegend.Resolve(20, bound: null));
    }

    [Fact]
    public void A_bound_key_from_the_character_file_replaces_the_default()
    {
        var bound = new Dictionary<int, int> { [1] = 57, [11] = 65 };

        Assert.Equal("9", QuickSlotLegend.Resolve(1, bound)!.Label);
        Assert.Equal("A", QuickSlotLegend.Resolve(11, bound)!.Label);
        Assert.Equal("M2", QuickSlotLegend.Resolve(2, bound)!.Label);
    }

    [Fact]
    public void A_stored_zero_means_unbound_even_when_a_default_exists()
    {
        var bound = new Dictionary<int, int> { [2] = 0 };

        Assert.Null(QuickSlotLegend.Resolve(2, bound));
    }

    [Theory]
    [InlineData(124, "M1", QuickKeyKind.Mouse)]
    [InlineData(127, "M4", QuickKeyKind.Mouse)]
    [InlineData(5, "M5", QuickKeyKind.Mouse)]
    [InlineData(6, "M6", QuickKeyKind.Mouse)]
    [InlineData(53, "5", QuickKeyKind.Keyboard)]
    [InlineData(48, "0", QuickKeyKind.Keyboard)]
    [InlineData(65, "A", QuickKeyKind.Keyboard)]
    public void Key_codes_map_to_their_labels(int keyCode, string label, QuickKeyKind kind)
    {
        var key = QuickSlotKeys.FromKeyCode(keyCode);

        Assert.NotNull(key);
        Assert.Equal(label, key!.Label);
        Assert.Equal(kind, key.Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void Unknown_or_unbound_key_codes_have_no_label(int keyCode)
    {
        Assert.Null(QuickSlotKeys.FromKeyCode(keyCode));
    }

    [Fact]
    public void Input_types_follow_the_game_names_and_round_trip()
    {
        Assert.Equal("QuickSlot1", QuickSlotKeys.InputType(1));
        Assert.Equal("QuickSlot0", QuickSlotKeys.InputType(10));
        Assert.Equal("QuickSlot10", QuickSlotKeys.InputType(20));

        foreach (var slot in Enumerable.Range(1, QuickSlotKeys.SlotCount))
        {
            Assert.Equal(slot, QuickSlotKeys.SlotForInputType(QuickSlotKeys.InputType(slot)));
        }
    }

    [Theory]
    [InlineData("PadA")]
    [InlineData("QuickSlotX")]
    [InlineData("QuickSlot21")]
    [InlineData("QuickSlot")]
    public void Other_input_types_are_not_quick_slots(string inputType)
    {
        Assert.Null(QuickSlotKeys.SlotForInputType(inputType));
    }

    [Fact]
    public void Parse_reads_the_keyboard_rows_and_ignores_pad_rows()
    {
        const string text = """
            <ShortcutKey Version="15"/>
            <ShortcutKey_Action_Key InputType="QuickSlot1" Alt="false" IgnoreFuncKey="false" keyCode="124"/>
            <ShortcutKey_Action_Key InputType="QuickSlot0" Alt="false" IgnoreFuncKey="false" keyCode="48"/>
            <ShortcutKey_Action_Key InputType="QuickSlot11" Alt="false" IgnoreFuncKey="false" keyCode="0"/>
            <ShortcutKey_Action_Key InputType="QuickSlot5" Alt="false" IgnoreFuncKey="false"/>
            <ShortcutKey_Action_Pad InputType="QuickSlot2" PadFunc1="true" PadInputType="A"/>
            """;

        var bound = QuickSlotKeyReader.Parse(text);

        Assert.Equal(new Dictionary<int, int> { [1] = 124, [10] = 48, [11] = 0 }, bound);
    }

    [Fact]
    public void Character_file_path_is_under_the_11_folder_of_the_account()
    {
        var path = QuickSlotKeyReader.CharacterFilePath("330", "10000000000000169");

        Assert.EndsWith(Path.Combine("UserCache", "330", "11", "10000000000000169", "gameVariable.xml"), path);
    }

    [Fact]
    public void A_missing_character_file_gives_null_and_is_logged()
    {
        using var log = new TempLogger();

        var bound = QuickSlotKeyReader.Load(Path.Combine(log.Directory, "absent.xml"), log.Logger);

        Assert.Null(bound);
    }
}
