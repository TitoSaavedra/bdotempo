using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

/// <summary>The Apply page view model: startup selection, spec switching, and profiles. The write flow is in ApplyComboWriteTests.</summary>
public sealed class ApplyComboViewModelTests : IDisposable
{
    private readonly TempLogger _log = new();
    private readonly ApplyTestHelpers.ProfileFolder _profiles;

    public ApplyComboViewModelTests()
    {
        _profiles = new ApplyTestHelpers.ProfileFolder(_log.Logger);
    }

    public void Dispose()
    {
        _profiles.Dispose();
        _log.Dispose();
    }

    private ApplyComboViewModel Create(IComboApplier? applier = null, ICombosCatalog? catalog = null, ComboProfileStore? store = null) => new(
        catalog ?? FakeCombosCatalog.Sample(),
        new NoIconAssets(),
        new MockComboDataSource(),
        store ?? _profiles.Store,
        applier ?? new RecordingApplier(),
        new KeyLocalizer(),
        ApplyTestHelpers.Culture,
        _log.Logger);

    private static SpecOptionViewModel Spec(ApplyComboViewModel vm, string id) => vm.Specs.Single(spec => spec.Id == id);

    [Fact]
    public async Task Startup_without_arguments_uses_the_first_characters_class_and_its_first_spec()
    {
        var vm = Create();

        await vm.InitializeAsync();

        Assert.Equal("Demo Tamer", vm.SelectedCharacter?.Name);
        Assert.Equal("tamer", vm.SelectedClass?.Slug);
        Assert.Equal("awakening", vm.SelectedSpec?.Id);
        Assert.True(vm.HasEditor);
        Assert.True(vm.CanRun);
    }

    [Fact]
    public async Task Startup_class_and_spec_arguments_select_them_and_the_character_of_that_class()
    {
        var vm = Create();

        await vm.InitializeAsync("shai", "talent");

        Assert.Equal("shai", vm.SelectedClass?.Slug);
        Assert.Equal("talent", vm.SelectedSpec?.Id);
        Assert.Equal("Demo Shai", vm.SelectedCharacter?.Name);
        Assert.Equal("Shai Talent", vm.Editor?.Title);
    }

    [Fact]
    public async Task Initialize_runs_once()
    {
        var vm = Create();
        await vm.InitializeAsync("tamer", "awakening");

        await vm.InitializeAsync("shai", "talent");

        Assert.Equal("tamer", vm.SelectedClass?.Slug);
    }

    [Fact]
    public async Task Choosing_a_character_selects_its_class()
    {
        var vm = Create();
        await vm.InitializeAsync("warrior", "awakening");

        await vm.SelectCharacterCommand.ExecuteAsync(vm.Characters.Single(item => item.Name == "Demo Tamer"));

        Assert.Equal("tamer", vm.SelectedClass?.Slug);
        Assert.Equal("Demo Tamer", vm.SelectedCharacter?.Name);
    }

    [Fact]
    public async Task Choosing_a_class_selects_its_first_spec_and_marks_only_that_class()
    {
        var vm = Create();
        await vm.InitializeAsync("warrior", "awakening");

        await vm.Classes.Single(item => item.Slug == "tamer").SelectCommand.ExecuteAsync(null);

        Assert.Equal("awakening", vm.SelectedSpec?.Id);
        Assert.Equal(["awakening", "succession"], vm.Specs.Select(spec => spec.Id));
        Assert.Equal(["tamer"], vm.Classes.Where(item => item.IsSelected).Select(item => item.Slug));
    }

    [Fact]
    public async Task Switching_spec_loads_that_spec_guide()
    {
        var vm = Create();
        await vm.InitializeAsync("tamer", "awakening");
        Assert.Equal("Tamer Awakening", vm.Editor?.Title);

        await Spec(vm, "succession").SelectCommand.ExecuteAsync(null);

        Assert.Equal("succession", vm.SelectedSpec?.Id);
        Assert.Equal("Tamer Succession", vm.Editor?.Title);
        Assert.True(Spec(vm, "succession").IsSelected);
        Assert.False(Spec(vm, "awakening").IsSelected);
    }

    [Fact]
    public async Task Shai_experts_share_a_guide_but_keep_their_own_profile_file()
    {
        var vm = Create();
        await vm.InitializeAsync("shai", "talent");
        vm.Editor!.AllSteps().First().Memo = "shared memo";
        await vm.SaveProfileCommand.ExecuteAsync(null);

        await Spec(vm, "azminek").SelectCommand.ExecuteAsync(null);

        Assert.Equal("azminek", vm.SelectedSpec?.Id);
        Assert.False(vm.Editor!.HasSavedProfile);
        Assert.Null(vm.Editor.AllSteps().First().Memo);
    }

    [Fact]
    public async Task Working_copy_is_kept_when_switching_away_and_back()
    {
        var vm = Create();
        await vm.InitializeAsync("tamer", "awakening");
        var first = vm.Editor;
        first!.AllSteps().First().CustomName = "Opener";

        await Spec(vm, "succession").SelectCommand.ExecuteAsync(null);
        await Spec(vm, "awakening").SelectCommand.ExecuteAsync(null);

        Assert.Same(first, vm.Editor);
        Assert.Equal("Opener", vm.Editor!.AllSteps().First().CustomName);
        Assert.True(vm.Editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task A_spec_without_a_guide_has_no_editor_and_is_reported_as_missing()
    {
        var catalog = new FakeCombosCatalog(
            [new ClassInfo(16, "tamer", new BilingualText("Tamer", "Domadora"), [new SpecInfo("awakening", new BilingualText("Awakening", "Despertar"), "tamer.missing")])],
            new Dictionary<(int, string), ComboGuide>());
        var vm = Create(catalog: catalog);

        await vm.InitializeAsync();

        Assert.Null(vm.Editor);
        Assert.False(vm.HasEditor);
        Assert.True(vm.IsGuideMissing);
        Assert.False(vm.CanRun);
    }

    [Fact]
    public async Task Profile_state_text_follows_the_guide_then_unsaved_then_saved()
    {
        var vm = Create();
        await vm.InitializeAsync("tamer", "awakening");
        Assert.Equal("Apply.StateBase", vm.ProfileStateText);

        vm.Editor!.AllSteps().First().Memo = "R 1";
        Assert.Equal("Apply.StateUnsaved", vm.ProfileStateText);

        await vm.SaveProfileCommand.ExecuteAsync(null);
        Assert.Equal("Apply.StateSaved", vm.ProfileStateText);
    }

    [Fact]
    public async Task Save_writes_the_profile_and_a_new_page_loads_it_over_the_guide()
    {
        var vm = Create();
        await vm.InitializeAsync("tamer", "awakening");
        var step = vm.Editor!.AllSteps().First();
        step.CustomName = "Intim.";
        step.Memo = "R 1";
        vm.Editor.AssignCooldown(step, 3);

        await vm.SaveProfileCommand.ExecuteAsync(null);

        Assert.True(vm.IsStatusOpen);
        Assert.Equal("Apply.StatusSaved", vm.StatusMessage);
        Assert.Equal(StatusSeverity.Success, vm.StatusSeverity);
        Assert.True(File.Exists(_profiles.Store.PathFor(16, "awakening")));

        var reopened = Create();
        await reopened.InitializeAsync("tamer", "awakening");
        var restored = reopened.Editor!.AllSteps().First();
        Assert.True(reopened.Editor.HasSavedProfile);
        Assert.Equal("Intim.", restored.CustomName);
        Assert.Equal("R 1", restored.Memo);
        Assert.Equal(3, restored.CooldownSlot);
        Assert.Equal("Apply.StateSaved", reopened.ProfileStateText);
    }

    [Fact]
    public async Task Restore_base_deletes_the_profile_and_goes_back_to_the_guide()
    {
        var vm = Create();
        await vm.InitializeAsync("tamer", "awakening");
        vm.Editor!.AllSteps().First().Memo = "R 1";
        await vm.SaveProfileCommand.ExecuteAsync(null);

        vm.RestoreBaseCommand.Execute(null);

        Assert.False(File.Exists(_profiles.Store.PathFor(16, "awakening")));
        Assert.False(vm.Editor!.HasSavedProfile);
        Assert.False(vm.Editor.HasUnsavedChanges);
        Assert.Null(vm.Editor.AllSteps().First().Memo);
        Assert.Equal("Apply.StatusRestored", vm.StatusMessage);
        Assert.Equal("Apply.StateBase", vm.ProfileStateText);
    }

    [Fact]
    public async Task A_failed_save_is_shown_as_an_error_and_logged()
    {
        // A file where the profile folder should be makes the save fail with an IOException.
        var blocked = Path.Combine(Path.GetTempPath(), "BdoComboSetup.Tests.Profiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.WriteAllText(blocked, "not a folder");
        try
        {
            var vm = Create(store: new ComboProfileStore(blocked, _log.Logger));
            await vm.InitializeAsync("tamer", "awakening");

            await vm.SaveProfileCommand.ExecuteAsync(null);

            Assert.Equal("Apply.StatusSaveFailed", vm.StatusMessage);
            Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
            Assert.Contains("\"event\":\"apply.profile.save_failed\"", File.ReadAllText(_log.Logger.FilePath));
        }
        finally
        {
            File.Delete(blocked);
        }
    }

    [Fact]
    public async Task A_failing_applier_is_reported_as_an_error_and_logged()
    {
        var vm = Create(applier: new ThrowingApplier());
        await vm.InitializeAsync("tamer", "awakening");

        await vm.DryRunCommand.ExecuteAsync(null);

        Assert.True(vm.IsStatusOpen);
        Assert.Equal("Apply.StatusFailed", vm.StatusMessage);
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.False(vm.IsReviewOpen);

        var logText = File.ReadAllText(_log.Logger.FilePath);
        Assert.Contains("\"event\":\"apply.failed\"", logText);
        Assert.Contains("\"level\":\"error\"", logText);
    }

    [Fact]
    public async Task Apply_and_dry_run_are_disabled_without_an_editor()
    {
        var catalog = new FakeCombosCatalog([], new Dictionary<(int, string), ComboGuide>());
        var vm = Create(catalog: catalog);

        await vm.InitializeAsync();

        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.False(vm.DryRunCommand.CanExecute(null));
        Assert.False(vm.SaveProfileCommand.CanExecute(null));
    }

    private sealed class ThrowingApplier : IComboApplier
    {
        public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");

        public Task<ComboApplyResult> DryRunAsync(ComboApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
