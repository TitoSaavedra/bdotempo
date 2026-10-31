using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

public sealed class PreviewViewModelTests : IDisposable
{
    private const double W = 3440;
    private const double H = 1440;

    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private AppSettingsStore Store() => new(Path.Combine(_log.Directory, "settings.json"), _log.Logger);

    private PreviewViewModel Create(
        GameUiLayoutSet? layouts = null,
        IReadOnlyDictionary<int, int>? quickKeys = null,
        PreviewStartup? startup = null,
        AppSettingsStore? store = null) => new(
            new ComboPreviewSlotSource(TestCombos.Tamer()),
            "Tamer",
            new GameDisplaySettings(3440, 1440, Windowed: true, UiScale: 1.0),
            new StubGameWindowSource(),
            layouts,
            quickKeys,
            store ?? Store(),
            startup ?? new PreviewStartup(GameUiLayoutId.General, ShowHiddenSlots: false, CooldownDemo: false, Zoom: null),
            new KeyLocalizer(),
            _log.Logger);

    /// <summary>A layout with every slot panel of the General block (all visible), except the indices in <paramref name="omit"/>.</summary>
    private static GameUiLayoutSet FullLayouts(IEnumerable<int>? omit = null, int? hiddenCooldown = null)
    {
        var skip = omit?.ToHashSet() ?? [];
        var rows = new List<string>();
        for (var number = 1; number <= 20; number++)
        {
            var index = GameSlotIndices.CooldownPanelIndex(number);
            if (!skip.Contains(index))
            {
                rows.Add(Row(index, show: number != hiddenCooldown, number));
            }
        }

        for (var number = 1; number <= 20; number++)
        {
            var index = GameSlotIndices.QuickPanelIndex(number);
            if (!skip.Contains(index))
            {
                rows.Add(Row(index, show: true, 20 + number));
            }
        }

        return Parse("<UIData Version=\"2\">\n" + string.Join("\n", rows) + "\n</UIData>\n");
    }

    private static string Row(int index, bool show, int seed)
    {
        // A grid of synthetic positions; the centre is stored the way the game stores it.
        var column = seed % 10;
        var row = seed / 10;
        var centreX = 900 + (column * 80);
        var centreY = 500 + (row * 80);
        return string.Create(CultureInfo.InvariantCulture,
            $"\t<UIData Index=\"{index}\" IsShow=\"{(show ? "true" : "false")}\" PendingType=\"LeftTop\" PosX=\"{centreX - 25}\" PosY=\"{centreY - 25}\" RelativePosX=\"{centreX / W:R}\" RelativePosY=\"{centreY / H:R}\"/>");
    }

    private static GameUiLayoutSet Parse(string text) => GameUiLayoutReader.Parse(text);

    [Fact]
    public void Starts_with_twenty_cooldown_and_twenty_quick_slots()
    {
        var vm = Create();

        Assert.Equal(20, vm.CooldownSlots.Count);
        Assert.Equal(20, vm.QuickSlots.Count);
        Assert.Equal(Enumerable.Range(1, 20), vm.CooldownSlots.Select(slot => slot.Number));
        Assert.Equal(Enumerable.Range(1, 20), vm.QuickSlots.Select(slot => slot.Position));
    }

    [Fact]
    public void Initial_selection_is_the_first_slot_with_a_skill()
    {
        var vm = Create();

        Assert.Same(vm.CooldownSlots[0], vm.SelectedSlot);
        Assert.True(vm.SelectedSlot!.IsSelected);
        Assert.True(vm.CanEdit);
        Assert.False(vm.HasPendingChanges);
    }

    [Fact]
    public void Slots_are_drawn_at_their_saved_positions()
    {
        var vm = Create(FullLayouts());

        // Synthetic grid: seed 1 is column 1, so the centre is 900 + 80.
        var cooldown = vm.CooldownSlots[0];
        Assert.True(cooldown.IsPresent);
        Assert.True(cooldown.IsDrawn);
        Assert.Equal(980, cooldown.Placement!.CentreX, precision: 6);
        Assert.Equal(955, cooldown.Offset.Left, precision: 6);   // centre minus half of the 50 px art
        Assert.Equal(50, cooldown.Width, precision: 6);
        Assert.Equal(65, vm.QuickSlots[0].Height, precision: 6);
    }

    [Fact]
    public void Hidden_slots_are_not_drawn_by_default_and_are_dimmed_when_shown()
    {
        var vm = Create(FullLayouts(hiddenCooldown: 3));
        var hidden = vm.CooldownSlots[2];

        Assert.True(hidden.IsHiddenPanel);
        Assert.False(hidden.IsDrawn);
        Assert.False(hidden.ShowsDashedOutline);

        vm.ShowHiddenSlots = true;

        Assert.True(hidden.IsDrawn);
        Assert.True(hidden.ShowsDashedOutline);
        Assert.Equal(PanelViewModel.HiddenOpacity, hidden.Opacity);
        Assert.Equal(1, vm.CooldownSlots[0].Opacity);
    }

    [Fact]
    public void Missing_rows_are_skipped_and_reported_in_the_status()
    {
        var vm = Create(FullLayouts(omit: [GameSlotIndices.CooldownPanelIndex(2)]));

        Assert.False(vm.CooldownSlots[1].IsPresent);
        Assert.False(vm.CooldownSlots[1].IsDrawn);
        Assert.Equal("Preview.Layout.MissingPanels(1)", vm.LayoutStatusText);
    }

    [Fact]
    public void Without_a_layout_file_the_status_says_so_and_nothing_is_drawn()
    {
        var vm = Create(layouts: null);

        Assert.Equal("Preview.Layout.FileMissing", vm.LayoutStatusText);
        Assert.All(vm.QuickSlots, quick => Assert.False(quick.IsDrawn));
    }

    [Fact]
    public void Presets_read_their_own_block_and_general_has_no_rows_for_them()
    {
        var text = "<UISettingPreset Version=\"1\">\n"
            + "\t<UISettingPreset0 Index=\"128\" IsShow=\"true\" PendingType=\"LeftTop\" PosX=\"1953\" PosY=\"696\" RelativePosX=\"0.575\" RelativePosY=\"0.500347\"/>\n"
            + "</UISettingPreset>\n";
        var layouts = Parse(text);

        var general = Create(layouts, startup: new PreviewStartup(GameUiLayoutId.General, false, false, null));
        Assert.False(general.CooldownSlots[0].IsPresent);

        general.SelectLayout(GameUiLayoutId.Preset1);

        Assert.Equal(GameUiLayoutId.Preset1, general.SelectedLayout);
        Assert.True(general.CooldownSlots[0].IsPresent);
    }

    [Fact]
    public void Quick_slot_legends_come_from_the_bound_keys_or_the_defaults()
    {
        var bound = new Dictionary<int, int> { [1] = 57 };

        var vm = Create(quickKeys: bound);

        Assert.Equal("9", vm.QuickSlots[0].KeyLabel);
        Assert.Equal("M2", vm.QuickSlots[1].KeyLabel);
        Assert.Equal("M1", Create().QuickSlots[0].KeyLabel);
        Assert.Equal(string.Empty, vm.QuickSlots[10].KeyLabel);
    }

    [Fact]
    public void Quick_slots_come_from_the_combo_quick_positions()
    {
        var vm = Create();

        Assert.Equal("Invocar a Heuklang", vm.QuickSlots[0].SkillName);    // position 1 holds cooldown slot 13
        Assert.Equal(13, vm.QuickSlots[0].CooldownNumber);
        Assert.False(vm.QuickSlots[10].HasSkill);                          // quick slots 11-20 have no source yet
    }

    [Fact]
    public void Cooldown_slots_know_their_quick_position()
    {
        var vm = Create();

        Assert.Equal(7, vm.CooldownSlots[0].QuickPosition);
        Assert.Equal(1, vm.CooldownSlots[12].QuickPosition);
        Assert.Equal(0, vm.CooldownSlots[6].QuickPosition);
    }

    [Fact]
    public void Selecting_a_quick_slot_selects_it_without_the_quick_editor()
    {
        var vm = Create();

        vm.SelectQuickSlotCommand.Execute(vm.QuickSlots[1]);

        Assert.Same(vm.QuickSlots[1], vm.SelectedQuickSlot);
        Assert.Null(vm.SelectedSlot);
        Assert.False(vm.IsEditorVisible);
        Assert.True(vm.QuickSlots[1].IsSelected);
    }

    [Fact]
    public void Saving_a_position_held_by_another_skill_swaps_them()
    {
        var vm = Create();
        vm.SelectSlotCommand.Execute(vm.CooldownSlots[0]);   // slot 1, quick position 7
        vm.DraftQuickPosition = 1;                            // slot 13 holds position 1

        vm.SaveChangesCommand.Execute(null);

        Assert.Equal(1, vm.CooldownSlots[0].QuickPosition);
        Assert.Equal(7, vm.CooldownSlots[12].QuickPosition);
        Assert.Equal("Flujo: Intimidación", vm.QuickSlots[0].SkillName);
        Assert.Equal(13, vm.QuickSlots[6].CooldownNumber);
        Assert.False(vm.HasPendingChanges);
    }

    [Fact]
    public void Clearing_a_quick_position_removes_the_skill_from_the_bar()
    {
        var vm = Create();
        vm.SelectSlotCommand.Execute(vm.CooldownSlots[0]);
        vm.DraftQuickPosition = 0;

        vm.SaveChangesCommand.Execute(null);

        Assert.Equal(0, vm.CooldownSlots[0].QuickPosition);
        Assert.False(vm.QuickSlots[6].HasSkill);
    }

    [Fact]
    public void An_empty_cooldown_slot_cannot_be_edited()
    {
        var vm = Create();

        vm.SelectSlotCommand.Execute(vm.CooldownSlots[19]);

        Assert.True(vm.CooldownSlots[19].IsEmpty);
        Assert.False(vm.CanEdit);
        Assert.False(vm.SaveChangesCommand.CanExecute(null));
    }

    [Fact]
    public void Zoom_starts_at_fit_and_wheel_steps_change_it_but_never_below_fit()
    {
        var vm = Create();
        vm.SetViewport(1720, 720);

        Assert.Equal(0.5, vm.FitScale, precision: 9);
        Assert.Equal(0.5, vm.ScreenScale, precision: 9);

        vm.ZoomBy(2);
        Assert.Equal(0.5 * CanvasZoom.StepFactor * CanvasZoom.StepFactor, vm.ScreenScale, precision: 9);

        vm.ZoomBy(-10);
        Assert.Equal(0.5, vm.ScreenScale, precision: 9);

        vm.ZoomBy(3);
        vm.FitToView();
        Assert.Equal(1, vm.Zoom);
    }

    [Fact]
    public void Startup_zoom_is_clamped_to_the_allowed_range()
    {
        var vm = Create(startup: new PreviewStartup(GameUiLayoutId.General, false, false, Zoom: 100));

        Assert.Equal(CanvasZoom.MaxZoom, vm.Zoom);
    }

    [Fact]
    public void Cooldown_demo_marks_the_first_slot_with_a_skill_only_when_on()
    {
        var vm = Create();
        Assert.False(vm.CooldownSlots[0].IsCoolingDown);

        vm.CooldownDemo = true;

        var demo = vm.CooldownSlots[0];
        Assert.True(demo.IsCoolingDown);
        Assert.Equal(PreviewViewModel.DemoCooldownRatio, demo.CooldownRatio);
        Assert.Equal(PreviewViewModel.DemoCooldownText, demo.CooldownText);
        Assert.False(vm.CooldownSlots[1].IsCoolingDown);

        vm.CooldownDemo = false;
        Assert.False(demo.IsCoolingDown);
    }

    [Fact]
    public void Background_falls_back_to_none_when_no_screenshot_exists()
    {
        var vm = Create();

        var expected = File.Exists(PreviewViewModel.DefaultBackgroundPath) ? BackgroundKind.Default : BackgroundKind.None;
        Assert.Equal(expected, vm.BackgroundKind);
    }

    [Fact]
    public void Layout_choice_is_saved_for_the_next_run()
    {
        var store = Store();
        var vm = Create(store: store);

        vm.SelectLayout(GameUiLayoutId.Preset2);

        Assert.Equal("Preset2", store.Settings.SelectedLayout);
    }
}
