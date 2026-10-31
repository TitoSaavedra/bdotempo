using BdoComboSetup.Core.Combo;
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

    [Fact]
    public void Defaults_to_awakening_with_all_options_checked_and_the_first_character()
    {
        var vm = Create(new StubComboApplier(_log.Logger));

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
        var vm = Create(new StubComboApplier(_log.Logger));

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
        var vm = Create(new StubComboApplier(_log.Logger));

        vm.IsSuccession = true;

        Assert.Equal(ComboMode.Succession, vm.Mode);
        Assert.Equal("Apply_SummaryTitle(Tamer,Mode_Succession)", vm.SummaryTitle);
    }

    [Fact]
    public async Task Apply_with_the_stub_reports_not_connected_as_a_warning()
    {
        var vm = Create(new StubComboApplier(_log.Logger));

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsStatusOpen);
        Assert.Equal("Apply_Status_NotConnected", vm.StatusMessage);
        Assert.Equal(StatusSeverity.Warning, vm.StatusSeverity);
    }

    [Fact]
    public async Task Dry_run_with_the_stub_reports_not_connected_as_a_warning()
    {
        var vm = Create(new StubComboApplier(_log.Logger));

        await vm.DryRunCommand.ExecuteAsync(null);

        Assert.True(vm.IsStatusOpen);
        Assert.Equal("Apply_Status_NotConnected", vm.StatusMessage);
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

    private sealed class ThrowingApplier : IComboApplier
    {
        public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
