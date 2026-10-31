using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Icons;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

public sealed class PreviewViewModelTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private string SettingsPath => Path.Combine(_log.Directory, "settings.json");

    private AppSettingsStore Store() => new(SettingsPath, _log.Logger);

    private IconCatalog EmptyIcons() => IconCatalog.Load(Path.Combine(_log.Directory, "none"), Path.Combine(_log.Directory, "none"), _log.Logger);

    private PreviewViewModel CreateViewModel(
        GameDisplaySettings? display = null,
        GameLayoutIndices? indices = null,
        GameUiLayoutSet? layouts = null,
        AppSettingsStore? store = null,
        IconCatalog? icons = null,
        GameUiLayoutId? initialLayout = null) => new(
            TestCombos.Tamer(),
            indices ?? GameLayoutIndices.Default,
            display,
            new StubGameWindowSource(),
            layouts,
            icons ?? EmptyIcons(),
            store ?? Store(),
            initialLayout,
            initialShowUiPanels: false,
            new KeyLocalizer(),
            _log.Logger);

    private static GameUiLayoutSet Layouts() => GameUiLayoutReader.Parse(UiLayoutFixtures.Sample);

    [Fact]
    public void Starts_with_20_cooldown_slots_and_9_quick_positions()
    {
        var vm = CreateViewModel();

        Assert.Equal(20, vm.CooldownSlots.Count);
        Assert.Equal(9, vm.QuickSlots.Count);
        Assert.Equal(Enumerable.Range(1, 20), vm.CooldownSlots.Select(slot => slot.Index));
    }

    [Fact]
    public void Initial_selection_is_the_first_filled_slot()
    {
        var vm = CreateViewModel();

        Assert.Same(vm.CooldownSlots[0], vm.SelectedSlot);
        Assert.True(vm.SelectedSlot!.IsSelected);
        Assert.True(vm.CanEdit);
        Assert.False(vm.HasPendingChanges);
    }

    [Fact]
    public void Selecting_a_slot_moves_the_selection_and_the_draft_position()
    {
        var vm = CreateViewModel();

        vm.SelectSlotCommand.Execute(vm.CooldownSlots[1]);

        Assert.Same(vm.CooldownSlots[1], vm.SelectedSlot);
        Assert.False(vm.CooldownSlots[0].IsSelected);
        Assert.True(vm.CooldownSlots[1].IsSelected);
        Assert.Equal(8, vm.DraftQuickPosition);
    }

    [Fact]
    public void Selecting_an_empty_slot_selects_it_but_disables_editing()
    {
        var vm = CreateViewModel();

        vm.SelectSlotCommand.Execute(vm.CooldownSlots[19]);

        Assert.True(vm.CooldownSlots[19].IsEmpty);
        Assert.Same(vm.CooldownSlots[19], vm.SelectedSlot);
        Assert.False(vm.CanEdit);
        Assert.False(vm.SaveChangesCommand.CanExecute(null));
    }

    [Fact]
    public void Quick_bar_maps_each_position_to_the_slot_that_holds_it()
    {
        var vm = CreateViewModel();

        Assert.Same(vm.CooldownSlots[12], vm.QuickSlots[0].Slot);  // position 1: slot 13
        Assert.Same(vm.CooldownSlots[13], vm.QuickSlots[1].Slot);  // position 2: slot 14
        Assert.Same(vm.CooldownSlots[2], vm.QuickSlots[2].Slot);   // position 3: slot 3
        Assert.Same(vm.CooldownSlots[0], vm.QuickSlots[6].Slot);   // position 7: slot 1
        Assert.Same(vm.CooldownSlots[1], vm.QuickSlots[7].Slot);   // position 8: slot 2
        Assert.Same(vm.CooldownSlots[8], vm.QuickSlots[8].Slot);   // position 9: slot 9
    }

    [Fact]
    public void Quick_bar_labels_follow_the_mouse_and_key_layout()
    {
        var vm = CreateViewModel();

        Assert.Equal(new[] { "M1", "M2", "M3", "M4", "Preview_KeyLabel(5)", "Preview_KeyLabel(6)", "M5", "M6", "Preview_KeyLabel(9)" },
            vm.QuickSlots.Select(quick => quick.Label));
    }

    [Fact]
    public void Clicking_a_quick_position_selects_its_slot()
    {
        var vm = CreateViewModel();
        vm.SelectSlotCommand.Execute(vm.CooldownSlots[5]);

        vm.SelectQuickSlotCommand.Execute(vm.QuickSlots[1]);

        Assert.Same(vm.CooldownSlots[13], vm.SelectedSlot);
    }

    [Fact]
    public void Saving_a_new_position_moves_the_quick_bar_entry()
    {
        var vm = CreateViewModel();
        vm.SelectSlotCommand.Execute(vm.CooldownSlots[4]);   // slot 5, no quick position
        vm.DraftQuickPosition = 4;

        Assert.True(vm.HasPendingChanges);
        Assert.True(vm.SaveChangesCommand.CanExecute(null));

        vm.SaveChangesCommand.Execute(null);

        Assert.Equal(4, vm.CooldownSlots[4].QuickPosition);
        Assert.False(vm.HasPendingChanges);
        Assert.Same(vm.CooldownSlots[4], vm.QuickSlots[3].Slot);
    }

    [Fact]
    public void Saving_a_position_held_by_another_slot_swaps_them()
    {
        var vm = CreateViewModel();
        vm.SelectSlotCommand.Execute(vm.CooldownSlots[0]);   // slot 1, position 7
        vm.DraftQuickPosition = 1;                            // slot 13 holds position 1

        vm.SaveChangesCommand.Execute(null);

        Assert.Equal(1, vm.CooldownSlots[0].QuickPosition);
        Assert.Equal(7, vm.CooldownSlots[12].QuickPosition);
        Assert.Same(vm.CooldownSlots[0], vm.QuickSlots[0].Slot);
        Assert.Same(vm.CooldownSlots[12], vm.QuickSlots[6].Slot);
    }

    [Fact]
    public void Saving_none_removes_the_slot_from_the_quick_bar()
    {
        var vm = CreateViewModel();
        vm.SelectSlotCommand.Execute(vm.CooldownSlots[1]);   // slot 2, position 8
        vm.DraftQuickPosition = 0;

        vm.SaveChangesCommand.Execute(null);

        Assert.Equal(0, vm.CooldownSlots[1].QuickPosition);
        Assert.Null(vm.QuickSlots[7].Slot);
        Assert.False(vm.QuickSlots[7].HasSlot);
    }

    [Fact]
    public void Reset_discards_the_draft_and_keeps_the_saved_position()
    {
        var vm = CreateViewModel();
        vm.SelectSlotCommand.Execute(vm.CooldownSlots[0]);
        vm.DraftQuickPosition = 2;
        Assert.True(vm.HasPendingChanges);

        vm.ResetCommand.Execute(null);

        Assert.Equal(7, vm.DraftQuickPosition);
        Assert.False(vm.HasPendingChanges);
        Assert.Equal(7, vm.CooldownSlots[0].QuickPosition);
    }

    [Fact]
    public void Game_pixel_canvas_uses_the_game_option_resolution()
    {
        var vm = CreateViewModel(new GameDisplaySettings(3440, 1440, Windowed: true, UiScale: 1.0));

        Assert.Equal(3440.0, vm.GameWidth);
        Assert.Equal(1440.0, vm.GameHeight);
        Assert.Equal(3440.0 / 1440.0, vm.CanvasAspectRatio, precision: 9);
        Assert.Equal(1.0, vm.UiScale);
    }

    [Fact]
    public void Canvas_falls_back_to_1920_by_1080_without_game_option()
    {
        var vm = CreateViewModel(display: null);

        Assert.Equal(PreviewViewModel.FallbackGameWidth, vm.GameWidth);
        Assert.Equal(PreviewViewModel.FallbackGameHeight, vm.GameHeight);
        Assert.Null(vm.DisplaySettings);
    }

    [Fact]
    public void Default_indices_leave_the_bars_unverified_with_quick_candidates()
    {
        var vm = CreateViewModel(display: new GameDisplaySettings(3440, 1440, Windowed: true, UiScale: 1.0), layouts: Layouts());

        Assert.Null(vm.CooldownBar.IndexUsed);
        Assert.Equal(BarSource.Candidates, vm.QuickBar.Source);
        Assert.False(vm.IsLayoutVerified);
        Assert.True(vm.CooldownBar.IsDraggable);
        Assert.True(vm.QuickBar.IsDraggable);
        Assert.Equal("Preview_LayoutUnknown", vm.LayoutStatusText);
    }

    [Fact]
    public void Without_a_layout_file_the_quick_bar_uses_the_generic_place()
    {
        var vm = CreateViewModel(layouts: null);

        Assert.Equal(BarSource.Default, vm.QuickBar.Source);
    }

    [Fact]
    public void Missing_layout_file_is_reported()
    {
        var vm = CreateViewModel(layouts: null);

        Assert.Equal("Preview_Layout_FileMissing", vm.LayoutStatusText);
        Assert.Empty(vm.UiPanels);
    }

    [Fact]
    public void Layout_file_fills_the_panel_overlay_for_the_selected_layout()
    {
        var vm = CreateViewModel(layouts: Layouts());

        Assert.Contains(vm.UiPanels, panel => panel.Label == "91" && panel.IsCandidate);
        Assert.DoesNotContain(vm.UiPanels, panel => panel.Label == "21");
    }

    [Fact]
    public void Selecting_a_layout_changes_the_panels_and_is_remembered()
    {
        var store = Store();
        var vm = CreateViewModel(layouts: Layouts(), store: store);
        var generalCount = vm.UiPanels.Count;

        vm.SelectLayout(GameUiLayoutId.Preset1);

        Assert.Equal(GameUiLayoutId.Preset1, vm.SelectedLayout);
        Assert.Equal("Preset1", Store().Settings.SelectedLayout);
        Assert.DoesNotContain(vm.UiPanels, panel => panel.Label == "2");
        Assert.NotEqual(generalCount, vm.UiPanels.Count);
    }

    [Fact]
    public void Layout_chosen_at_startup_is_not_written_to_the_settings()
    {
        var store = Store();

        var vm = CreateViewModel(store: store, initialLayout: GameUiLayoutId.Preset2);

        Assert.Equal(GameUiLayoutId.Preset2, vm.SelectedLayout);
        Assert.Null(store.Settings.SelectedLayout);
    }

    [Fact]
    public void Verified_indices_make_the_bars_exact_and_locked()
    {
        var indices = new GameLayoutIndices(CooldownBarIndex: 13, QuickSlotBarIndex: 91, QuickSlotBarCandidates: []);

        var vm = CreateViewModel(
            display: new GameDisplaySettings(3440, 1440, Windowed: true, UiScale: 1.0),
            layouts: Layouts(),
            indices: indices);

        Assert.True(vm.IsLayoutVerified);
        Assert.Equal(BarSource.UiData, vm.CooldownBar.Source);
        Assert.Equal(new PixelOffset(1524, 1264), vm.CooldownBar.Offset);
        Assert.False(vm.CooldownBar.IsDraggable);
        Assert.Equal(string.Empty, vm.LayoutStatusText);

        vm.MoveBarTo(BarKind.Cooldown, 10, 10);
        Assert.Equal(new PixelOffset(1524, 1264), vm.CooldownBar.Offset);
    }

    [Fact]
    public void Dragged_bar_is_clamped_to_the_screen_and_its_slots_follow()
    {
        var vm = CreateViewModel(display: new GameDisplaySettings(3440, 1440, Windowed: true, UiScale: 1.0));

        vm.MoveBarTo(BarKind.Cooldown, 5000, -40);

        Assert.Equal(new PixelOffset(3440 - vm.CooldownBar.Width, 0), vm.CooldownBar.Offset);

        // Slot offsets are relative to the bar, which the view moves; they only change with the grid.
        Assert.Equal(new PixelOffset(0, 0), vm.CooldownSlots[0].Offset);
        Assert.Equal(vm.CooldownBar.PitchX, vm.CooldownSlots[1].Offset.Left, precision: 6);
        Assert.Equal(vm.CooldownBar.PitchY, vm.CooldownSlots[10].Offset.Top, precision: 6);
    }

    [Fact]
    public void Quick_slots_follow_the_quick_bar_pitch()
    {
        var vm = CreateViewModel();

        vm.MoveBarTo(BarKind.Quick, 100, 200);

        Assert.Equal(new PixelOffset(100, 200), vm.QuickBar.Offset);
        Assert.Equal(new PixelOffset(3 * 72, 0), vm.QuickSlots[3].Offset);
    }

    [Fact]
    public void Committed_position_is_saved_for_the_layout_and_restored_by_a_new_view_model()
    {
        var display = new GameDisplaySettings(3440, 1440, Windowed: true, UiScale: 1.0);
        var store = Store();
        var vm = CreateViewModel(display: display, store: store);
        vm.MoveBarTo(BarKind.Cooldown, 1000, 500);

        vm.CommitBarPosition(BarKind.Cooldown);

        Assert.Equal(BarSource.Manual, vm.CooldownBar.Source);
        Assert.Equal(1000.0, vm.CooldownBar.Offset.Left, precision: 6);
        Assert.Equal(500.0, vm.CooldownBar.Offset.Top, precision: 6);
        Assert.True(Store().Settings.ManualBarPositions.ContainsKey("General.Cooldown"));

        var restarted = CreateViewModel(display: display, store: Store());
        Assert.Equal(BarSource.Manual, restarted.CooldownBar.Source);
        Assert.Equal(1000.0 / 3440.0, Store().Settings.ManualBarPositions["General.Cooldown"].X, precision: 9);
        Assert.Equal(1000.0, restarted.CooldownBar.Offset.Left, precision: 6);
        Assert.Equal(500.0, restarted.CooldownBar.Offset.Top, precision: 6);
    }

    [Fact]
    public void Saved_position_belongs_to_one_layout_only()
    {
        var store = Store();
        var vm = CreateViewModel(store: store, layouts: Layouts());
        vm.MoveBarTo(BarKind.Quick, 300, 300);
        vm.CommitBarPosition(BarKind.Quick);

        vm.SelectLayout(GameUiLayoutId.Preset1);

        Assert.Equal(BarSource.Candidates, vm.QuickBar.Source);
        Assert.NotEqual(new PixelOffset(300, 300), vm.QuickBar.Offset);
    }

    [Fact]
    public void Reset_bar_position_forgets_the_saved_position()
    {
        var store = Store();
        var vm = CreateViewModel(store: store);
        vm.MoveBarTo(BarKind.Cooldown, 1000, 500);
        vm.CommitBarPosition(BarKind.Cooldown);

        vm.ResetBarPosition(BarKind.Cooldown);

        Assert.Equal(BarSource.Default, vm.CooldownBar.Source);
        Assert.False(Store().Settings.ManualBarPositions.ContainsKey("General.Cooldown"));
    }

    [Fact]
    public void Chosen_background_must_exist_and_is_remembered()
    {
        var store = Store();
        var vm = CreateViewModel(store: store);
        var picture = Path.Combine(_log.Directory, "picked.png");
        File.WriteAllBytes(picture, [0x89, 0x50, 0x4E, 0x47]);

        Assert.False(vm.SetBackgroundPath(Path.Combine(_log.Directory, "missing.png")));
        Assert.True(vm.SetBackgroundPath(picture));

        Assert.Equal(BackgroundKind.Custom, vm.BackgroundKind);
        Assert.Equal(picture, vm.BackgroundPath);
        Assert.Equal(picture, Store().Settings.BackgroundImagePath);
    }

    [Fact]
    public void Resetting_the_background_forgets_the_choice()
    {
        var store = Store();
        var vm = CreateViewModel(store: store);
        var picture = Path.Combine(_log.Directory, "picked.png");
        File.WriteAllBytes(picture, [0x89, 0x50, 0x4E, 0x47]);
        vm.SetBackgroundPath(picture);

        vm.ResetBackground();

        Assert.Null(Store().Settings.BackgroundImagePath);
        Assert.NotEqual(BackgroundKind.Custom, vm.BackgroundKind);
    }

    [Fact]
    public void Skill_icon_comes_from_the_catalog_and_missing_icons_leave_the_glyph()
    {
        var root = Path.Combine(_log.Directory, "output");
        Directory.CreateDirectory(Path.Combine(root, "icons", "skills", "Tamer"));
        File.WriteAllText(Path.Combine(root, "skills.json"),
            """{ "1937": { "class_id": "16", "class_table_name": "Tamer", "icon_path": "icons/skills/Tamer/pbw_skill_1937.webp" } }""");
        File.WriteAllBytes(Path.Combine(root, "icons", "skills", "Tamer", "pbw_skill_1937.webp"), [0x52, 0x49, 0x46, 0x46]);
        var icons = IconCatalog.Load(root, Path.Combine(_log.Directory, "none"), _log.Logger);

        var vm = CreateViewModel(icons: icons);

        Assert.Equal(Path.Combine(root, "icons", "skills", "Tamer", "pbw_skill_1937.webp"), vm.CooldownSlots[0].IconPath);
        Assert.True(vm.CooldownSlots[0].HasIcon);
        Assert.Null(vm.CooldownSlots[1].IconPath);   // skill 1926 is not in the catalog
    }

    [Fact]
    public void Cooldown_slots_fill_the_rows_and_extra_slots_are_hidden()
    {
        var store = Store();
        store.Update(settings => settings with { CooldownRows = 1, CooldownColumns = 10 });

        var vm = CreateViewModel(store: store);

        Assert.True(vm.CooldownSlots[9].IsPlaced);
        Assert.False(vm.CooldownSlots[10].IsPlaced);
        Assert.Equal(vm.CooldownSlots[0].Offset.Top, vm.CooldownSlots[9].Offset.Top);
    }
}
