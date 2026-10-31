using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

/// <summary>
/// The Preview page: slots at their saved positions, the combo of the Apply page, quick keys, the hidden-slots toggle,
/// zoom and the remembered layout. Synthetic data only: the demo layouts and the demo characters.
/// </summary>
public sealed class PreviewViewModelTests : IDisposable
{
    private const string TamerPcid = "10000000000000011";
    private const string WarriorPcid = "10000000000000001";

    private readonly TempLogger _log = new();
    private readonly ApplyTestHelpers.ProfileFolder _profiles;
    private readonly List<IDisposable> _owned = [];

    public PreviewViewModelTests()
    {
        _profiles = new ApplyTestHelpers.ProfileFolder(_log.Logger);
    }

    public void Dispose()
    {
        foreach (var item in _owned)
        {
            item.Dispose();
        }

        _profiles.Dispose();
        _log.Dispose();
    }

    private ApplyComboViewModel Apply() => new(
        FakeCombosCatalog.Sample(),
        new NoIconAssets(),
        new FakeCharacterSource(),
        _profiles.Store,
        new RecordingApplier(),
        TestConfirmation.Accepting(_log.Logger),
        new KeyLocalizer(),
        ApplyTestHelpers.Culture,
        _log.Logger);

    private AppSettingsStore Store() => new(Path.Combine(_log.Directory, "settings.json"), _log.Logger);

    private PreviewViewModel Create(
        ApplyComboViewModel apply,
        bool withLayoutFile = true,
        Func<CharacterOption?, IReadOnlyDictionary<int, int>?>? boundKeysOf = null,
        PreviewStartup? startup = null,
        AppSettingsStore? store = null)
    {
        var source = new ComboPreviewSlotSource(apply, boundKeysOf ?? (_ => null));
        _owned.Add(source);
        var vm = new PreviewViewModel(
            source,
            DemoGameLayouts.Display,
            new BlackGameScreenSource(),
            withLayoutFile ? DemoGameLayouts.Create() : null,
            store ?? Store(),
            startup ?? new PreviewStartup(null, ShowHiddenSlots: false, CooldownDemo: false, Zoom: null),
            new KeyLocalizer(),
            _log.Logger);
        _owned.Add(vm);
        return vm;
    }

    [Fact]
    public void Starts_with_twenty_cooldown_and_twenty_quick_slots()
    {
        var vm = Create(Apply());

        Assert.Equal(20, vm.CooldownSlots.Count);
        Assert.Equal(20, vm.QuickSlots.Count);
    }

    [Fact]
    public void Before_the_apply_page_loads_a_combo_every_slot_is_empty_and_nothing_breaks()
    {
        var vm = Create(Apply());

        Assert.All(vm.CooldownSlots, slot => Assert.False(slot.HasSkill));
        Assert.All(vm.QuickSlots, quick => Assert.False(quick.HasSkill));
    }

    [Fact]
    public async Task Shows_the_combo_being_edited_on_the_apply_page()
    {
        var apply = Apply();
        var vm = Create(apply);

        await apply.InitializeAsync("tamer", "awakening");

        var editor = apply.Editor!;
        var first = editor.AllSteps().First(step => step.CooldownSlot == 1);
        Assert.True(vm.CooldownSlots[0].HasSkill);
        Assert.Equal(first.SkillName, vm.CooldownSlots[0].Title);
        Assert.Equal(
            editor.AllSteps().Count(step => step.QuickPosition is not null),
            vm.QuickSlots.Count(quick => quick.HasSkill));
    }

    [Fact]
    public async Task Editing_a_step_on_the_apply_page_moves_it_on_the_preview()
    {
        var apply = Apply();
        var vm = Create(apply);
        await apply.InitializeAsync("tamer", "awakening");
        var editor = apply.Editor!;
        var step = editor.AllSteps().First(item => item.CooldownSlot == 1);

        editor.AssignCooldown(step, 20);

        Assert.False(vm.CooldownSlots[0].HasSkill);
        Assert.Equal(step.SkillName, vm.CooldownSlots[19].Title);
    }

    [Fact]
    public async Task Quick_slots_show_the_bound_key_of_the_character_and_the_default_otherwise()
    {
        var apply = Apply();
        await apply.RefreshCharactersAsync();
        var keys = new Dictionary<string, IReadOnlyDictionary<int, int>>
        {
            [TamerPcid] = new Dictionary<int, int> { [1] = 53 },
        };
        var vm = Create(apply, boundKeysOf: character => character is not null && keys.TryGetValue(character.Pcid, out var bound) ? bound : null);

        Assert.Equal("5", vm.QuickSlots[0].KeyLabel);
        Assert.Equal("M2", vm.QuickSlots[1].KeyLabel);
        Assert.Equal("0", vm.QuickSlots[9].KeyLabel);
    }

    [Fact]
    public async Task Changing_the_character_reads_its_quick_keys_again()
    {
        var apply = Apply();
        await apply.RefreshCharactersAsync();
        var keys = new Dictionary<string, IReadOnlyDictionary<int, int>>
        {
            [TamerPcid] = new Dictionary<int, int> { [1] = 53 },
            [WarriorPcid] = new Dictionary<int, int> { [1] = 54 },
        };
        var vm = Create(apply, boundKeysOf: character => character is not null && keys.TryGetValue(character.Pcid, out var bound) ? bound : null);
        Assert.Equal("5", vm.QuickSlots[0].KeyLabel);

        apply.SelectedCharacter = apply.Characters.Single(character => character.Pcid == WarriorPcid);

        Assert.Equal("6", vm.QuickSlots[0].KeyLabel);
    }

    [Fact]
    public void Slots_are_drawn_at_their_saved_positions()
    {
        var vm = Create(Apply());

        // General: the cooldown bar starts at (1443, 1218) and the quick bar at (1443, centre 1390 minus half of 65).
        Assert.Equal(1443, vm.CooldownSlots[0].Offset.Left, 6);
        Assert.Equal(1218, vm.CooldownSlots[0].Offset.Top, 6);
        Assert.Equal(50, vm.CooldownSlots[0].Width, 6);
        Assert.Equal(50, vm.CooldownSlots[0].Height, 6);
        Assert.Equal(1443, vm.QuickSlots[0].Offset.Left, 6);
        Assert.Equal(1357.5, vm.QuickSlots[0].Offset.Top, 6);
        Assert.Equal(65, vm.QuickSlots[0].Height, 6);
    }

    [Fact]
    public void Hidden_slots_are_not_drawn_until_the_toggle_is_on_and_then_dimmed_with_a_dashed_outline()
    {
        var vm = Create(Apply());

        vm.SelectLayout(GameUiLayoutId.Preset2);
        var hidden = vm.QuickSlots[5];
        Assert.True(hidden.IsHiddenPanel);
        Assert.False(hidden.IsDrawn);

        vm.ShowHiddenSlots = true;

        Assert.True(hidden.IsDrawn);
        Assert.True(hidden.ShowsDashedOutline);
        Assert.Equal(PanelViewModel.HiddenOpacity, hidden.Opacity);
    }

    [Fact]
    public void Quick_slots_11_to_20_are_hidden_in_the_general_layout_by_default()
    {
        var vm = Create(Apply());

        Assert.All(vm.QuickSlots.Skip(10), quick => Assert.False(quick.IsDrawn));
        Assert.All(vm.QuickSlots.Take(10), quick => Assert.True(quick.IsDrawn));
        Assert.False(vm.ShowHiddenSlots);
    }

    [Fact]
    public void Without_a_layout_file_the_status_says_so_and_no_slot_is_drawn()
    {
        var vm = Create(Apply(), withLayoutFile: false);

        Assert.Equal("Preview.Layout.FileMissing", vm.LayoutStatusText);
        Assert.All(vm.CooldownSlots, slot => Assert.False(slot.IsDrawn));
        Assert.All(vm.QuickSlots, quick => Assert.False(quick.IsDrawn));
    }

    [Fact]
    public void Each_preset_has_its_own_positions_and_the_general_layout_keeps_its_own()
    {
        var vm = Create(Apply());
        var general = vm.CooldownSlots[0].Offset;

        vm.SelectLayout(GameUiLayoutId.Preset1);
        var preset1 = vm.CooldownSlots[0].Offset;
        vm.SelectLayout(GameUiLayoutId.General);

        Assert.NotEqual(general, preset1);
        Assert.Equal(general, vm.CooldownSlots[0].Offset);
    }

    [Fact]
    public void Zoom_starts_at_fit_and_never_goes_below_it()
    {
        var vm = Create(Apply());
        vm.SetViewport(1720, 720);

        Assert.Equal(0.5, vm.FitScale, 6);
        Assert.Equal(0.5, vm.ScreenScale, 6);

        vm.ZoomBy(-3);
        Assert.Equal(CanvasZoom.MinZoom, vm.Zoom);

        vm.ZoomBy(2);
        Assert.True(vm.Zoom > CanvasZoom.MinZoom);
        Assert.Equal(vm.FitScale * vm.Zoom, vm.ScreenScale, 9);

        vm.FitToView();
        Assert.Equal(CanvasZoom.MinZoom, vm.Zoom);
    }

    [Fact]
    public async Task Cooldown_demo_marks_the_first_slot_with_a_skill_only_when_on()
    {
        var apply = Apply();
        await apply.InitializeAsync("tamer", "awakening");
        var vm = Create(apply, startup: new PreviewStartup(null, ShowHiddenSlots: false, CooldownDemo: true, Zoom: null));

        var first = vm.CooldownSlots.First(slot => slot.HasSkill);
        Assert.Equal(PreviewViewModel.DemoCooldownRatio, first.CooldownRatio, 6);
        Assert.True(first.IsCoolingDown);
        Assert.Equal(1, vm.CooldownSlots.Count(slot => slot.IsCoolingDown));

        vm.CooldownDemo = false;
        Assert.Equal(0, vm.CooldownSlots.Count(slot => slot.IsCoolingDown));
    }

    [Fact]
    public void The_layout_choice_is_remembered_for_the_next_run()
    {
        var store = Store();
        var first = Create(Apply(), store: store);

        first.SelectLayout(GameUiLayoutId.Preset3);
        var next = Create(Apply(), store: Store());

        Assert.Equal("Preset3", store.Settings.SelectedLayout);
        Assert.Equal(GameUiLayoutId.Preset3, next.SelectedLayout);
        Assert.Equal(GameUiLayoutId.Preset3, next.SelectedLayoutOption.Id);
    }

    [Fact]
    public async Task After_dispose_the_preview_no_longer_follows_the_apply_page()
    {
        var apply = Apply();
        var vm = Create(apply);
        vm.Dispose();

        await apply.InitializeAsync("tamer", "awakening");

        Assert.All(vm.CooldownSlots, slot => Assert.False(slot.HasSkill));
    }

    [Fact]
    public void Without_a_captured_game_the_status_line_says_the_background_is_black()
    {
        var vm = Create(Apply());

        Assert.Equal("Preview.Capture.NotRunning", vm.CaptureStatusText);
    }
}
