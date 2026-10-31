using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Tests;

public sealed class GameUiLayoutReaderTests
{
    private static readonly GameUiLayoutSet Set = GameUiLayoutReader.Parse(UiLayoutFixtures.Sample);

    [Theory]
    [InlineData(13)]
    [InlineData(85)]
    [InlineData(91)]
    [InlineData(94)]
    [InlineData(100)]
    [InlineData(113)]
    [InlineData(139)]
    [InlineData(141)]
    [InlineData(9)]
    [InlineData(21)]
    public void Converted_general_rows_match_the_preset_block_for_real_rows(int index)
    {
        // Preset 1 is read from UISettingPreset0, whose rows already store LeftTop absolute coordinates.
        // They are an independent check of the anchor rule used for the General rows.
        var general = Set.Get(GameUiLayoutId.General).Find(index);
        Assert.NotNull(general);
        Assert.NotNull(Set.Get(GameUiLayoutId.Preset1).Find(index));

        var topLeft = UiPanelGeometry.TopLeft(general!, UiLayoutFixtures.GameWidth, UiLayoutFixtures.GameHeight);

        Assert.Equal(ExpectedPresetRow(index), topLeft);
    }

    [Fact]
    public void Bottom_anchor_uses_the_screen_height_minus_pos_y()
    {
        // Index 13: General LeftBottom PosY=176 becomes LeftTop Y=1264 on a 1440 px screen (the preset block says 1264).
        var panel = Set.Get(GameUiLayoutId.General).Find(13)!;

        var topLeft = UiPanelGeometry.TopLeft(panel, UiLayoutFixtures.GameWidth, UiLayoutFixtures.GameHeight);

        Assert.Equal(new UiPoint(1524, 1264), topLeft);
    }

    [Fact]
    public void Right_anchor_uses_the_screen_width_minus_pos_x()
    {
        // Index 94: General RightBottom PosX=1706 becomes LeftTop X=1734 on a 3440 px screen (the preset block says 1734).
        var panel = Set.Get(GameUiLayoutId.General).Find(94)!;

        var topLeft = UiPanelGeometry.TopLeft(panel, UiLayoutFixtures.GameWidth, UiLayoutFixtures.GameHeight);

        Assert.Equal(new UiPoint(1734, 1361), topLeft);
    }

    [Theory]
    [InlineData(32, 0)]
    [InlineData(113, null)]
    [InlineData(21, null)]
    public void Stored_relative_centre_agrees_with_the_panel_size(int index, int? window)
    {
        // RelativePosX/Y are the panel centre divided by the screen size. Checked only where SizeX/SizeY are stored.
        var panel = Set.Get(GameUiLayoutId.General).Panels.First(row => row.Index == index && row.WindowIndex == window);
        var rel = RelativeCentre(index, window);

        var topLeft = UiPanelGeometry.TopLeft(panel, UiLayoutFixtures.GameWidth, UiLayoutFixtures.GameHeight)!.Value;
        var centreX = topLeft.X + panel.SizeX!.Value / 2;
        var centreY = topLeft.Y + panel.SizeY!.Value / 2;

        Assert.True(Math.Abs(centreX - rel.X * UiLayoutFixtures.GameWidth) < 1.5, $"x {centreX} vs {rel.X * UiLayoutFixtures.GameWidth}");
        Assert.True(Math.Abs(centreY - rel.Y * UiLayoutFixtures.GameHeight) < 1.5, $"y {centreY} vs {rel.Y * UiLayoutFixtures.GameHeight}");
    }

    [Fact]
    public void General_block_keeps_every_top_level_row_including_chat_windows()
    {
        var general = Set.Get(GameUiLayoutId.General);

        Assert.Equal(2, general.Panels.Count(panel => panel.Index == 32));
        Assert.Contains(general.Panels, panel => panel.Index == 2 && !panel.HasPosition);
    }

    [Fact]
    public void Find_prefers_a_row_without_window_index_and_falls_back_to_chat_windows()
    {
        var general = Set.Get(GameUiLayoutId.General);

        // Both index 32 rows are chat windows (WindowIndex 0 and 1), so the first one is returned.
        Assert.Equal(0, general.Find(32)!.WindowIndex);
        Assert.Null(general.Find(999));
    }

    [Fact]
    public void Preset_blocks_are_read_from_their_UISettingPreset_number()
    {
        var preset1 = Set.Get(GameUiLayoutId.Preset1);
        var preset2 = Set.Get(GameUiLayoutId.Preset2);

        // Preset 1 = UISettingPreset0 (10 rows in the fixture), Preset 2 = UISettingPreset1 (2 rows), Preset 3 has no block.
        Assert.Equal(10, preset1.Panels.Count);
        Assert.True(preset1.Find(13)!.IsShow);
        Assert.Equal(2, preset2.Panels.Count);
        Assert.Equal(1531.0, preset2.Find(13)!.PosX);
        Assert.False(preset2.Find(13)!.IsShow);
        Assert.False(Set.Get(GameUiLayoutId.Preset3).HasPanels);
    }

    [Fact]
    public void Preset_rows_are_already_left_top_absolute()
    {
        var row = Set.Get(GameUiLayoutId.Preset1).Find(91)!;

        Assert.Equal(UiAnchor.LeftTop, row.Anchor);
        Assert.Equal(new UiPoint(1522, 1362), UiPanelGeometry.TopLeft(row, UiLayoutFixtures.GameWidth, UiLayoutFixtures.GameHeight));
    }

    [Fact]
    public void Rows_with_an_index_that_does_not_fit_are_counted_as_malformed()
    {
        Assert.Equal(1, Set.MalformedRows);
    }

    [Fact]
    public void Panels_without_a_position_have_no_top_left()
    {
        var row = Set.Get(GameUiLayoutId.General).Find(2)!;

        Assert.False(row.HasPosition);
        Assert.Null(UiPanelGeometry.TopLeft(row, UiLayoutFixtures.GameWidth, UiLayoutFixtures.GameHeight));
    }

    [Fact]
    public void Text_without_a_general_block_gives_an_empty_general_layout()
    {
        var set = GameUiLayoutReader.Parse(UiLayoutFixtures.PresetOnly);

        Assert.False(set.Get(GameUiLayoutId.General).HasPanels);
        Assert.Equal(10.0, set.Get(GameUiLayoutId.Preset3).Find(13)!.PosX);
        Assert.Equal(20.0, set.Get(GameUiLayoutId.Preset3).Find(13)!.PosY);
    }

    [Fact]
    public void Preset_numbers_map_to_the_blocks_in_one_place()
    {
        Assert.Equal(0, GameUiLayoutBlocks.PresetBlockNumbers[GameUiLayoutId.Preset1]);
        Assert.Equal(1, GameUiLayoutBlocks.PresetBlockNumbers[GameUiLayoutId.Preset2]);
        Assert.Equal(2, GameUiLayoutBlocks.PresetBlockNumbers[GameUiLayoutId.Preset3]);
    }

    /// <summary>Expected LeftTop position of an index in UISettingPreset0 (Preset 1) of the sample.</summary>
    private static UiPoint ExpectedPresetRow(int index) => index switch
    {
        9 => new UiPoint(3007, 20),
        21 => new UiPoint(3076, 621),
        13 => new UiPoint(1524, 1264),
        85 => new UiPoint(1953, 1272),
        100 => new UiPoint(1449, 1360),
        91 => new UiPoint(1522, 1362),
        94 => new UiPoint(1734, 1361),
        113 => new UiPoint(3073, 510),
        139 => new UiPoint(1, 88),
        141 => new UiPoint(2507, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Not in the sample."),
    };

    /// <summary>RelativePosX/Y as stored in the sample file (fractions of the 3440x1440 screen).</summary>
    private static UiPoint RelativeCentre(int index, int? window) => (index, window) switch
    {
        (32, 0) => new UiPoint(0.09912790358, 0.7487855554),
        (113, _) => new UiPoint(0.9376453757, 0.3813323975),
        (21, _) => new UiPoint(0.9385174513, 0.6894517541),
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Not in the sample."),
    };
}
