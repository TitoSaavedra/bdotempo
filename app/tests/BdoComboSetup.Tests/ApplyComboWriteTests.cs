using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Operations;
using BdoComboSetup.Core.ViewModels;

using BdoComboSetup.Core.Confirmation;

namespace BdoComboSetup.Tests;

/// <summary>
/// The write flow of the Apply page: the review (dry run) before anything is written, the write of the reviewed request,
/// the refusal after an edit, and the mapping from the working copy to the applier's request. Uses a recording applier;
/// nothing touches a game file.
/// </summary>
public sealed class ApplyComboWriteTests : IDisposable
{
    private readonly TempLogger _log = new();
    private readonly ApplyTestHelpers.ProfileFolder _profiles;

    public ApplyComboWriteTests()
    {
        _profiles = new ApplyTestHelpers.ProfileFolder(_log.Logger);
    }

    public void Dispose()
    {
        _profiles.Dispose();
        _log.Dispose();
    }

    private ApplyComboViewModel Create(RecordingApplier applier, WriteConfirmation? confirmation = null) => new(
        FakeCombosCatalog.Sample(),
        new NoIconAssets(),
        new FakeCharacterSource(),
        _profiles.Store,
        applier,
        confirmation ?? TestConfirmation.Accepting(_log.Logger),
        new KeyLocalizer(),
        ApplyTestHelpers.Culture,
        _log.Logger);

    private static ComboApplyResult DryRunResult(bool changed) => new(
        OperationOutcome.DryRun,
        OperationProblem.None,
        null,
        new CharacterDiff(
            "10000000000000011",
            "gameVariable.xml",
            changed,
            changed ? [new SlotChange(0, 0, 1937, string.Empty, "Flujo: Intimidación")] : [],
            [],
            []),
        null,
        null,
        []);

    private static ComboApplyResult WrittenResult() => new(
        OperationOutcome.Written,
        OperationProblem.None,
        null,
        null,
        "20261031T120000Z",
        "C:/backups/20261031T120000Z",
        []);

    [Fact]
    public async Task Apply_to_character_opens_the_review_with_the_dry_run_diff_and_writes_nothing()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Single(applier.DryRuns);
        Assert.Empty(applier.Writes);
        Assert.True(vm.IsReviewOpen);
        Assert.True(vm.ReviewHasChanges);
        Assert.Single(vm.ReviewLines);
        Assert.Equal("Apply.Review.Summary(1)", vm.ReviewSummary);
        Assert.True(vm.ConfirmWriteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Preview_changes_opens_the_review_without_a_write_button()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");

        await vm.DryRunCommand.ExecuteAsync(null);

        Assert.True(vm.IsReviewOpen);
        Assert.False(vm.ReviewHasChanges);
        Assert.False(vm.ConfirmWriteCommand.CanExecute(null));
        Assert.Empty(applier.Writes);
    }

    [Fact]
    public async Task A_review_without_changes_has_nothing_to_write()
    {
        var applier = new RecordingApplier(DryRunResult(changed: false));
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsReviewOpen);
        Assert.False(vm.ReviewHasChanges);
        Assert.Equal("Apply.Review.NoChanges", vm.ReviewSummary);
        Assert.Empty(vm.ReviewLines);
    }

    [Fact]
    public async Task Confirming_the_review_writes_the_reviewed_request_once_and_closes_the_review()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true), WrittenResult());
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.ConfirmWriteCommand.ExecuteAsync(null);

        var written = Assert.Single(applier.Writes);
        var reviewed = applier.DryRuns[0];
        Assert.Equal(reviewed.Pcid, written.Pcid);
        Assert.True(reviewed.CooldownSlots.SequenceEqual(written.CooldownSlots));
        Assert.True(reviewed.QuickSlots.SequenceEqual(written.QuickSlots));
        Assert.Equal(StatusSeverity.Success, vm.StatusSeverity);
        Assert.Contains("Apply.Result.Backup", vm.StatusMessage);
        Assert.False(vm.IsReviewOpen);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_write_is_refused_when_the_working_copy_changed_after_the_review()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true), WrittenResult());
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");
        await vm.ApplyCommand.ExecuteAsync(null);

        var step = vm.Editor!.AllSteps().First(item => item.CooldownSlot is not null);
        step.Memo = "changed after the review";
        await vm.ConfirmWriteCommand.ExecuteAsync(null);

        Assert.Empty(applier.Writes);
        Assert.False(vm.IsReviewOpen);
        Assert.Equal("Apply.StatusStale", vm.StatusMessage);
        Assert.Equal(StatusSeverity.Warning, vm.StatusSeverity);
    }

    [Fact]
    public async Task Changing_the_character_closes_the_review()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.SelectedCharacter = vm.Characters.Last();

        Assert.False(vm.IsReviewOpen);
        Assert.Empty(vm.ReviewLines);
    }

    [Fact]
    public async Task Changing_a_write_option_closes_the_review()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");
        await vm.ApplyCommand.ExecuteAsync(null);

        vm.IncludeMemos = false;

        Assert.False(vm.IsReviewOpen);
    }

    [Fact]
    public async Task A_refused_dry_run_is_a_warning_and_opens_no_review()
    {
        var refused = new ComboApplyResult(
            OperationOutcome.Refused,
            OperationProblem.SkillMismatch,
            "slot 1 (skillNo 1937): table group does not match",
            null,
            null,
            null,
            []);
        var vm = Create(new RecordingApplier(refused));
        await vm.InitializeAsync("tamer", "awakening");

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.False(vm.IsReviewOpen);
        Assert.Equal(StatusSeverity.Warning, vm.StatusSeverity);
        Assert.Contains("Apply.Result.Refused", vm.StatusMessage);
        Assert.Contains("Apply.Problem.SkillMismatch", vm.StatusMessage);
    }

    [Fact]
    public async Task The_request_has_the_bar_slots_zero_based_with_the_game_names_and_the_memos()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");

        await vm.DryRunCommand.ExecuteAsync(null);

        var request = applier.DryRuns.Single();
        Assert.Equal("10000000000000011", request.Pcid);
        Assert.Equal(16, request.ClassId);
        Assert.True(request.WriteCooldownBar && request.WriteMemos && request.WriteQuickSlots);

        // The default layout puts the combo steps in slots 1, 2 and 3; the buff's Heuklang is already placed in slot 3.
        var first = request.CooldownSlots.First(slot => slot.Slot == 0);
        Assert.Equal(100, first.SkillNo);
        Assert.Equal("Flujo: Intimidación", first.SkillName);
        Assert.Null(first.Memo);

        var second = request.CooldownSlots.First(slot => slot.Slot == 1);
        Assert.Equal(200, second.SkillNo);
        Assert.Equal("R 2", second.Memo);
    }

    [Fact]
    public async Task Turning_off_memos_sends_no_memo_text()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var vm = Create(applier);
        await vm.InitializeAsync("tamer", "awakening");
        vm.IncludeMemos = false;

        await vm.DryRunCommand.ExecuteAsync(null);

        var request = applier.DryRuns.Single();
        Assert.False(request.WriteMemos);
        Assert.All(request.CooldownSlots, slot => Assert.Null(slot.Memo));
    }

    [Fact]
    public async Task Mobility_steps_are_written_only_when_the_user_gives_them_a_slot()
    {
        var catalog = new FakeCombosCatalog(
            [new ClassInfo(16, "tamer", new BilingualText("Tamer", "Domadora"), [new SpecInfo("awakening", new BilingualText("Awakening", "Despertar"), "tamer.awakening")])],
            new Dictionary<(int, string), ComboGuide> { [(16, "awakening")] = ComboTestData.GuideWithMobility() });
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var vm = new ApplyComboViewModel(
            catalog,
            new NoIconAssets(),
            new FakeCharacterSource(),
            _profiles.Store,
            applier,
            TestConfirmation.Accepting(_log.Logger),
            new KeyLocalizer(),
            ApplyTestHelpers.Culture,
            _log.Logger);
        await vm.InitializeAsync("tamer", "awakening");

        await vm.DryRunCommand.ExecuteAsync(null);
        Assert.DoesNotContain(applier.DryRuns[0].CooldownSlots, slot => slot.SkillNo == 600);

        var butterfly = vm.Editor!.ManualChains.Single().Steps.First();
        vm.Editor.AssignCooldown(butterfly, 15);
        await vm.DryRunCommand.ExecuteAsync(null);

        var manual = applier.DryRuns[1].CooldownSlots.Single(slot => slot.SkillNo == 600);
        Assert.Equal(14, manual.Slot);
    }

    [Fact]
    public void The_builder_skips_steps_without_a_skill_and_quick_slots_follow_their_position()
    {
        var logger = new TempLogger();
        using var disposeLogger = logger;
        var editor = ComboEditorViewModel.Create(
            16,
            "awakening",
            ComboTestData.TamerGuide(),
            profile: null,
            new KeyLocalizer(),
            ApplyTestHelpers.Culture,
            logger.Logger);
        var step = editor.AllSteps().First(item => item.PrimarySkill?.SkillNo == 200);
        editor.AssignQuick(step, 2);

        var request = ComboApplyRequestBuilder.Build(editor, "1", new ComboWriteOptions(QuickSlots: true));

        var quick = Assert.Single(request.QuickSlots);
        Assert.Equal(2, quick.Position);
        Assert.Equal(200, quick.SkillNo);
    }

    [Fact]
    public async Task The_demo_applier_describes_a_dry_run_and_never_writes()
    {
        var demo = new DemoComboApplier(_log.Logger);
        var request = new ComboApplyRequest(
            "10000000000000011",
            16,
            [new CooldownSlot(0, 1937, "Flujo: Intimidación", "R 1")],
            [new QuickSlotAssignment(2, 1937)]);

        var dry = await demo.DryRunAsync(request);
        var write = await demo.ApplyAsync(request);

        Assert.Equal(OperationOutcome.DryRun, dry.Outcome);
        Assert.True(dry.Diff!.Changed);
        Assert.Single(dry.Diff.Slots);
        Assert.Single(dry.Diff.Memos);
        Assert.Single(dry.Diff.QuickSlots);
        Assert.Equal(OperationOutcome.Refused, write.Outcome);
        Assert.Null(write.BackupPath);
        Assert.Null(write.Diff);
    }

    [Fact]
    public async Task Cancel_in_the_reminder_writes_nothing_and_keeps_the_review_open()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: false, DontShowAgain: true));
        var setting = new FakeWriteHintSetting();
        var vm = Create(applier, new WriteConfirmation(setting, prompt, _log.Logger));
        await vm.InitializeAsync("tamer", "awakening");
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.ConfirmWriteCommand.ExecuteAsync(null);

        Assert.Equal(1, prompt.Shown);
        Assert.Empty(applier.Writes);
        Assert.True(vm.IsReviewOpen);
        Assert.False(setting.SkipWriteHint);
    }

    [Fact]
    public async Task Continue_in_the_reminder_writes_the_reviewed_changes_once()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: true, DontShowAgain: false));
        var vm = Create(applier, new WriteConfirmation(new FakeWriteHintSetting(), prompt, _log.Logger));
        await vm.InitializeAsync("tamer", "awakening");
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.ConfirmWriteCommand.ExecuteAsync(null);

        Assert.Equal(1, prompt.Shown);
        Assert.Single(applier.Writes);
    }

    [Fact]
    public async Task With_the_skip_flag_the_write_goes_on_without_the_reminder()
    {
        var applier = new RecordingApplier(DryRunResult(changed: true));
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: false, DontShowAgain: false));
        var vm = Create(applier, new WriteConfirmation(new FakeWriteHintSetting { SkipWriteHint = true }, prompt, _log.Logger));
        await vm.InitializeAsync("tamer", "awakening");
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.ConfirmWriteCommand.ExecuteAsync(null);

        Assert.Equal(0, prompt.Shown);
        Assert.Single(applier.Writes);
    }
}
