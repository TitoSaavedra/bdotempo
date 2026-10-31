using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Tests;

/// <summary>The synthetic layouts of the demo mode: complete, deterministic, and different from each other.</summary>
public sealed class DemoGameLayoutsTests
{
    [Fact]
    public void Create_gives_every_layout_id()
    {
        var set = DemoGameLayouts.Create();

        Assert.Equal(4, set.Layouts.Count);
        Assert.Equal(0, set.MalformedRows);
        foreach (var id in Enum.GetValues<GameUiLayoutId>())
        {
            Assert.True(set.Get(id).HasPanels);
        }
    }

    [Theory]
    [InlineData(GameUiLayoutId.General)]
    [InlineData(GameUiLayoutId.Preset1)]
    [InlineData(GameUiLayoutId.Preset2)]
    [InlineData(GameUiLayoutId.Preset3)]
    public void Every_layout_places_all_forty_slot_panels(GameUiLayoutId id)
    {
        var placements = GameSlotLayoutResolver.Resolve(DemoGameLayouts.Create().Get(id), DemoGameLayouts.GameWidth, DemoGameLayouts.GameHeight);

        Assert.Equal(40, placements.Count);
        Assert.All(placements, placement => Assert.True(placement.Present));
    }

    [Fact]
    public void General_hides_quick_slots_11_to_20_and_shows_the_first_ten()
    {
        var general = DemoGameLayouts.Create().Get(GameUiLayoutId.General);

        for (var number = 1; number <= 20; number++)
        {
            var panel = general.Find(GameSlotIndices.QuickPanelIndex(number));
            Assert.NotNull(panel);
            Assert.Equal(number <= 10, panel.IsShow);
        }
    }

    [Fact]
    public void Presets_move_the_cooldown_bar_away_from_general()
    {
        var set = DemoGameLayouts.Create();
        var general = GameSlotLayoutResolver.Resolve(set.Get(GameUiLayoutId.General), DemoGameLayouts.GameWidth, DemoGameLayouts.GameHeight)[0];

        foreach (var id in new[] { GameUiLayoutId.Preset1, GameUiLayoutId.Preset2, GameUiLayoutId.Preset3 })
        {
            var placement = GameSlotLayoutResolver.Resolve(set.Get(id), DemoGameLayouts.GameWidth, DemoGameLayouts.GameHeight)[0];
            Assert.False(placement.CentreX == general.CentreX && placement.CentreY == general.CentreY, $"{id} repeats the General position");
        }
    }

    [Fact]
    public void Preset1_hides_cooldown_slot_five_so_the_hidden_toggle_has_something_to_show()
    {
        var preset1 = DemoGameLayouts.Create().Get(GameUiLayoutId.Preset1);

        Assert.False(preset1.Find(GameSlotIndices.CooldownPanelIndex(5))!.IsShow);
        Assert.True(preset1.Find(GameSlotIndices.CooldownPanelIndex(4))!.IsShow);
    }
}
