using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Operations;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

public sealed class ApplyComboViewModelTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private ApplyComboViewModel Create(IComboApplier applier) => new(
        new MockComboDataSource(),
        TestCombos.Tamer(),
        applier,
        new KeyLocalizer(),
        _log.Logger);

    private static ComboApplyResult Outcome(OperationOutcome outcome, OperationProblem problem = OperationProblem.None, string? detail = null) =>
        new(outcome, problem, detail, null, null, null, []);

    [Fact]
    public void Defaults_to_awakening_with_all_options_checked_and_the_first_character()
    {
        var vm = Create(new FakeApplier());

        Assert.Equal(ComboMode.Awakening, vm.Mode);
        Assert.True(vm.IsAwakening);
        Assert.False(vm.IsSuccession);
        Assert.True(vm.IncludeCooldownBar);
        Assert.True(vm.IncludeMemos);
        Assert.True(vm.IncludeQuickSlots);
        Assert.Equal("AhrinSong", vm.SelectedCharacter?.Name);
        Assert.Equal("Tamer", vm.SelectedClassName);
        Assert.True(vm.CanRun);
    }

    [Fact]
    public void Summary_counts_come_from_the_combo()
    {
        var vm = Create(new FakeApplier());

        Assert.Equal(12, vm.ComboSkillCount);
        Assert.Equal(3, vm.PriorityBuffCount);
        Assert.Equal(9, vm.QuickSlotCount);
        Assert.Equal("Apply_ComboSkillsCount(12)", vm.ComboSkillsText);
        Assert.Equal("Apply_PriorityBuffsCount(3)", vm.PriorityBuffsText);
        Assert.Equal("Apply_QuickSlotsCount(9)", vm.QuickSlotsText);
    }

    [Fact]
    public void Changing_mode_updates_the_summary_title()
    {
        var vm = Create(new FakeApplier());

        vm.IsSuccession = true;

        Assert.Equal(ComboMode.Succession, vm.Mode);
        Assert.Equal("Apply_SummaryTitle(Tamer,Mode_Succession)", vm.SummaryTitle);
    }

    [Fact]
    public async Task Apply_sends_the_combo_as_cooldown_slots_and_quick_slots_for_the_selected_character()
    {
        var applier = new FakeApplier { Result = Outcome(OperationOutcome.Written) };
        var vm = Create(applier);

        await vm.ApplyCommand.ExecuteAsync(null);

        var request = Assert.Single(applier.Applied);
        Assert.Empty(applier.DryRuns);
        Assert.Equal("10000000006346933", request.Pcid);
        Assert.Equal(16, request.ClassId);
        Assert.True(request.WriteCooldownBar);
        Assert.True(request.WriteMemos);
        Assert.True(request.WriteQuickSlots);

        // UI slots 1, 2, 8, 13, 14 become XML slots 0, 1, 7, 12, 13; the empty ones are not sent.
        Assert.Equal(new[] { 0, 1, 7, 12, 13 }, request.CooldownSlots.Select(s => s.Slot).ToArray());
        Assert.Equal(1937, request.CooldownSlots[0].SkillNo);
        Assert.Equal("R 1", request.CooldownSlots[0].Memo);
        Assert.Equal("Flujo: Intimidación", request.CooldownSlots[0].SkillName);

        Assert.Equal(
            new (int Position, int SkillNo)[] { (7, 1937), (8, 1932), (1, 1347), (2, 231) },
            request.QuickSlots.Select(q => (q.Position, SkillNo: q.SkillNo!.Value)).ToArray());
    }

    [Fact]
    public async Task Turning_quick_slots_off_sends_no_quick_assignments()
    {
        var applier = new FakeApplier { Result = Outcome(OperationOutcome.Written) };
        var vm = Create(applier);
        vm.IncludeQuickSlots = false;

        await vm.ApplyCommand.ExecuteAsync(null);

        var request = Assert.Single(applier.Applied);
        Assert.False(request.WriteQuickSlots);
        Assert.Empty(request.QuickSlots);
    }

    [Fact]
    public async Task Dry_run_calls_the_dry_run_and_shows_the_summary_without_applying()
    {
        var diff = new CharacterDiff(
            "10000000006346933",
            @"C:\data\gameVariable.xml",
            true,
            [new SlotChange(0, 0, 1937, "(empty)", "Flujo: Intimidación")],
            [new MemoChange(1937, null, "R 1", MemoStatus.Added)],
            []);
        var applier = new FakeApplier
        {
            Result = new ComboApplyResult(OperationOutcome.DryRun, OperationProblem.None, null, diff, null, null, []),
        };
        var vm = Create(applier);

        await vm.DryRunCommand.ExecuteAsync(null);

        Assert.Empty(applier.Applied);
        Assert.Single(applier.DryRuns);
        Assert.True(vm.IsStatusOpen);
        Assert.Equal(StatusSeverity.Information, vm.StatusSeverity);
        Assert.Contains("Apply_Result_DryRun(Apply_Result_Summary(1,1,0,0))", vm.StatusMessage);
    }

    [Fact]
    public async Task Dry_run_without_changes_says_so()
    {
        var applier = new FakeApplier
        {
            Result = new ComboApplyResult(
                OperationOutcome.DryRun,
                OperationProblem.None,
                null,
                new CharacterDiff("10000000006346933", "x", false, [], [], []),
                null,
                null,
                []),
        };
        var vm = Create(applier);

        await vm.DryRunCommand.ExecuteAsync(null);

        Assert.Equal("Apply_Result_DryRunUnchanged", vm.StatusMessage);
    }

    [Fact]
    public async Task A_written_result_shows_the_summary_and_the_backup_path()
    {
        var applier = new FakeApplier
        {
            Result = new ComboApplyResult(
                OperationOutcome.Written,
                OperationProblem.None,
                null,
                null,
                "20261031T120000Z",
                @"C:\data\_backups\20261031T120000Z",
                []),
        };
        var vm = Create(applier);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(StatusSeverity.Information, vm.StatusSeverity);
        Assert.Contains("Apply_Result_Written", vm.StatusMessage);
        Assert.Contains(@"Apply_Result_Backup(C:\data\_backups\20261031T120000Z)", vm.StatusMessage);
    }

    [Fact]
    public async Task A_refused_result_is_a_warning_with_the_problem_and_its_detail()
    {
        var applier = new FakeApplier
        {
            Result = Outcome(OperationOutcome.Refused, OperationProblem.GameRunning, "PCID 1 was modified 3s ago"),
        };
        var vm = Create(applier);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(StatusSeverity.Warning, vm.StatusSeverity);
        Assert.Contains("Apply_Result_Refused(Apply_ProblemDetail(Apply_Problem_GameRunning,PCID 1 was modified 3s ago))", vm.StatusMessage);
    }

    [Fact]
    public async Task A_rolled_back_result_is_an_error()
    {
        var applier = new FakeApplier
        {
            Result = Outcome(OperationOutcome.RolledBack, OperationProblem.ValidationFailed, "slot list"),
        };
        var vm = Create(applier);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.Contains("Apply_Result_RolledBack", vm.StatusMessage);
    }

    [Fact]
    public async Task A_failing_applier_is_reported_as_an_error_and_logged()
    {
        var vm = Create(new ThrowingApplier());

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsStatusOpen);
        Assert.Equal("Apply_Status_Failed", vm.StatusMessage);
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);

        var logText = File.ReadAllText(_log.Logger.FilePath);
        Assert.Contains("\"event\":\"apply.failed\"", logText);
        Assert.Contains("\"level\":\"error\"", logText);
    }

    private sealed class FakeApplier : IComboApplier
    {
        public List<ComboApplyRequest> Applied { get; } = [];

        public List<ComboApplyRequest> DryRuns { get; } = [];

        public ComboApplyResult Result { get; init; } = Outcome(OperationOutcome.DryRun);

        public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
        {
            Applied.Add(request);
            return Task.FromResult(Result);
        }

        public Task<ComboApplyResult> DryRunAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
        {
            DryRuns.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingApplier : IComboApplier
    {
        public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");

        public Task<ComboApplyResult> DryRunAsync(ComboApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
