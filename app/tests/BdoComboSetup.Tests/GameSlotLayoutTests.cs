using System.Globalization;
using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Tests;

/// <summary>Slot geometry from synthetic rows in the real gamevariable.xml format (no real account data).</summary>
public sealed class GameSlotLayoutTests
{
    private const double W = 3440;
    private const double H = 1440;

    [Fact]
    public void Panel_indices_match_the_game_enum_for_both_bars()
    {
        Assert.Equal(128, GameSlotIndices.CooldownPanelIndex(1));
        Assert.Equal(137, GameSlotIndices.CooldownPanelIndex(10));
        Assert.Equal(148, GameSlotIndices.CooldownPanelIndex(11));
        Assert.Equal(157, GameSlotIndices.CooldownPanelIndex(20));
        Assert.Equal(91, GameSlotIndices.QuickPanelIndex(1));
        Assert.Equal(100, GameSlotIndices.QuickPanelIndex(10));
        Assert.Equal(101, GameSlotIndices.QuickPanelIndex(11));
        Assert.Equal(110, GameSlotIndices.QuickPanelIndex(20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Out_of_range_slot_numbers_are_rejected(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameSlotIndices.CooldownPanelIndex(number));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameSlotIndices.QuickPanelIndex(number));
    }

    [Fact]
    public void Resolve_gives_twenty_cooldown_then_twenty_quick_placements()
    {
        var placements = GameSlotLayoutResolver.Resolve(null, W, H);

        Assert.Equal(40, placements.Count);
        Assert.Equal(Enumerable.Range(1, 20), placements.Take(20).Select(p => p.Number));
        Assert.All(placements.Take(20), p => Assert.Equal(SlotKind.Cooldown, p.Kind));
        Assert.Equal(Enumerable.Range(1, 20), placements.Skip(20).Select(p => p.Number));
        Assert.All(placements.Skip(20), p => Assert.Equal(SlotKind.Quick, p.Kind));
    }

    [Fact]
    public void Cooldown_slot_centre_and_size_come_from_the_stored_centre()
    {
        // LeftTop corner (1000, 700); centre (1025, 725) means a 50 x 50 panel.
        var layout = Layout(Row(128, true, "LeftTop", 1000, 700, 1025 / W, 725 / H));

        var placement = Find(layout, SlotKind.Cooldown, 1);

        Assert.True(placement.Present);
        Assert.Equal(128, placement.PanelIndex);
        Assert.Equal(1025, placement.CentreX, precision: 6);
        Assert.Equal(725, placement.CentreY, precision: 6);
        Assert.Equal(50, placement.Width, precision: 6);
        Assert.Equal(50, placement.Height, precision: 6);
    }

    [Fact]
    public void Bottom_anchored_quick_slot_uses_the_screen_height_minus_pos_y()
    {
        // Index 100 (quick slot 10): LeftBottom PosY=80 gives a top edge at 1360; the centre is 1391.5.
        var layout = Layout(Row(100, true, "LeftBottom", 1449, 80, 1474 / W, 1391.5 / H));

        var placement = Find(layout, SlotKind.Quick, 10);

        Assert.Equal(50, placement.Width, precision: 6);
        Assert.Equal(63, placement.Height, precision: 6);
        Assert.Equal(1474, placement.CentreX, precision: 6);
        Assert.Equal(1391.5, placement.CentreY, precision: 6);
    }

    [Fact]
    public void Right_anchored_panel_is_measured_from_its_left_edge()
    {
        // RightTop PosX=1487 on a 3440 px screen: left edge 3440 - 1487 = 1953; centre 1978 gives a width of 50.
        // Y is measured from PosY (top): top edge 696, centre 720 gives a height of 48.
        var layout = Layout(Row(128, true, "RightTop", 1487, 696, 1978 / W, 720 / H));

        var placement = Find(layout, SlotKind.Cooldown, 1);

        Assert.Equal(50, placement.Width, precision: 6);
        Assert.Equal(48, placement.Height, precision: 6);
    }

    [Fact]
    public void Hidden_panel_keeps_its_saved_position()
    {
        var layout = Layout(Row(101, false, "LeftTop", 722, 574, 747 / W, 599 / H));

        var placement = Find(layout, SlotKind.Quick, 11);

        Assert.True(placement.Present);
        Assert.False(placement.IsShow);
        Assert.Equal(747, placement.CentreX, precision: 6);
    }

    [Fact]
    public void Missing_row_is_not_present_and_keeps_the_nominal_size()
    {
        var layout = Layout(Row(128, true, "LeftTop", 1000, 700, 1025 / W, 725 / H));

        var placement = Find(layout, SlotKind.Cooldown, 2);

        Assert.False(placement.Present);
        Assert.Equal(GameSlotLayoutResolver.CooldownNominalWidth, placement.Width);
        Assert.Equal(GameSlotLayoutResolver.CooldownNominalHeight, placement.Height);
    }

    [Fact]
    public void Missing_layout_places_nothing()
    {
        var placements = GameSlotLayoutResolver.Resolve(null, W, H);

        Assert.All(placements, p => Assert.False(p.Present));
    }

    [Fact]
    public void Row_without_a_stored_centre_uses_its_size_and_is_centred_inside_it()
    {
        var layout = GameUiLayoutReader.Parse(
            "<UIData Version=\"2\">\n\t<UIData Index=\"128\" IsShow=\"true\" PendingType=\"LeftTop\" PosX=\"1000\" PosY=\"700\" SizeX=\"40\" SizeY=\"40\"/>\n</UIData>\n")
            .Get(GameUiLayoutId.General);

        var placement = Find(layout, SlotKind.Cooldown, 1);

        Assert.True(placement.Present);
        Assert.Equal(40, placement.Width);
        Assert.Equal(1020, placement.CentreX, precision: 6);
        Assert.Equal(720, placement.CentreY, precision: 6);
    }

    [Fact]
    public void Row_with_a_zero_centre_falls_back_to_the_nominal_size()
    {
        var layout = Layout(Row(129, true, "RightTop", 433, 20, 0, 0));

        var placement = Find(layout, SlotKind.Cooldown, 2);

        Assert.True(placement.Present);
        Assert.Equal(GameSlotLayoutResolver.CooldownNominalWidth, placement.Width);
    }

    private static GameSlotPlacement Find(GameUiLayout layout, SlotKind kind, int number) =>
        GameSlotLayoutResolver.Resolve(layout, W, H).Single(p => p.Kind == kind && p.Number == number);

    /// <summary>A General block with the given rows, in the same layout as gamevariable.xml.</summary>
    private static GameUiLayout Layout(params string[] rows) =>
        GameUiLayoutReader.Parse("<UIData Version=\"2\">\n" + string.Join("\n", rows) + "\n</UIData>\n")
            .Get(GameUiLayoutId.General);

    private static string Row(int index, bool show, string anchor, double posX, double posY, double relX, double relY) =>
        string.Create(CultureInfo.InvariantCulture,
            $"\t<UIData Index=\"{index}\" IsShow=\"{(show ? "true" : "false")}\" PendingType=\"{anchor}\" PosX=\"{posX}\" PosY=\"{posY}\" RelativePosX=\"{relX:R}\" RelativePosY=\"{relY:R}\"/>");
}
