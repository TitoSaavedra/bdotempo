using BdoComboSetup.Core.Tour;
using Xunit;

namespace BdoComboSetup.Tests;

/// <summary>The rules of the guided tour (no UI): order, finish and skip, the drag wait and the Apply block.</summary>
public sealed class TourFlowTests
{
    private const int DragStepIndex = 1;

    /// <summary>Starts a flow and goes to the step at <paramref name="index"/>, doing the action of the waiting step on the way.</summary>
    private static TourFlow FlowAt(int index, bool hasCombo = true)
    {
        var flow = new TourFlow();
        flow.Start(hasCombo);
        while (flow.StepNumber - 1 < index)
        {
            flow.CompleteAction();
            Assert.True(flow.Next());
        }

        return flow;
    }

    [Fact]
    public void Start_GoesToFirstStep_AndIsActive()
    {
        var flow = new TourFlow();

        flow.Start(hasCombo: true);

        Assert.True(flow.IsActive);
        Assert.Equal(1, flow.StepNumber);
        Assert.Equal("character", flow.Current?.Id);
        Assert.Equal(5, flow.StepCount);
    }

    [Fact]
    public void Next_AdvancesThroughAllFiveStepsInOrder_AndTheLastNextFinishes()
    {
        var flow = new TourFlow();
        var ended = new List<TourEndReason>();
        flow.Ended += (_, e) => ended.Add(e.Reason);
        flow.Start(hasCombo: true);

        var ids = new List<string>();
        while (flow.IsActive)
        {
            ids.Add(flow.Current!.Id);
            flow.CompleteAction();
            flow.Next();
        }

        Assert.Equal(new[] { "character", "drag", "editor", "apply", "settings" }, ids);
        Assert.Equal(new[] { TourEndReason.Finished }, ended);
        Assert.False(flow.IsActive);
        Assert.Null(flow.Current);
    }

    [Fact]
    public void SkipStep_GoesToTheNextStep_AndTheTourStaysActive()
    {
        var flow = FlowAt(0);

        Assert.True(flow.SkipStep());

        Assert.True(flow.IsActive);
        Assert.Equal(2, flow.StepNumber);
    }

    [Fact]
    public void SkipStep_OnTheWaitingDragStep_MovesOnWithoutTheAction()
    {
        var flow = FlowAt(DragStepIndex);
        Assert.True(flow.IsWaitingForAction);

        Assert.True(flow.SkipStep());

        Assert.Equal("editor", flow.Current?.Id);
        Assert.False(flow.IsWaitingForAction);
    }

    [Fact]
    public void SkipStep_OnTheLastStep_FinishesTheTour()
    {
        var flow = FlowAt(4);
        var ended = new List<TourEndReason>();
        flow.Ended += (_, e) => ended.Add(e.Reason);

        Assert.True(flow.SkipStep());

        Assert.Equal(new[] { TourEndReason.Finished }, ended);
        Assert.False(flow.IsActive);
    }

    [Fact]
    public void SkipStep_WhenNotActive_DoesNothing()
    {
        var flow = new TourFlow();

        Assert.False(flow.SkipStep());
        Assert.False(flow.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Skip_AtAnyStep_EndsWithSkipped(int index)
    {
        var flow = FlowAt(index);
        var ended = new List<TourEndReason>();
        flow.Ended += (_, e) => ended.Add(e.Reason);

        flow.Skip();

        Assert.Equal(new[] { TourEndReason.Skipped }, ended);
        Assert.False(flow.IsActive);
    }

    [Fact]
    public void DragStep_RefusesNext_UntilCompleteAction()
    {
        var flow = FlowAt(DragStepIndex);

        Assert.True(flow.IsWaitingForAction);
        Assert.False(flow.CanAdvance);
        Assert.False(flow.Next());
        Assert.Equal("drag", flow.Current?.Id);

        flow.CompleteAction();

        Assert.False(flow.IsWaitingForAction);
        Assert.True(flow.CanAdvance);
        Assert.True(flow.Next());
        Assert.Equal("editor", flow.Current?.Id);
    }

    [Fact]
    public void DragStep_DoesNotWait_WhenNoComboIsLoaded()
    {
        var flow = FlowAt(DragStepIndex, hasCombo: false);

        Assert.Equal("drag", flow.Current?.Id);
        Assert.False(flow.IsWaitingForAction);
        Assert.True(flow.Next());
        Assert.Equal("editor", flow.Current?.Id);
    }

    [Fact]
    public void DragStep_StartsWaiting_WhenTheComboLoadsWhileOnIt()
    {
        var flow = FlowAt(DragStepIndex, hasCombo: false);

        flow.UpdateCombo(hasCombo: true);

        Assert.True(flow.IsWaitingForAction);
        Assert.False(flow.Next());
    }

    [Fact]
    public void IsApplyBlocked_IsTrueWhileActive_AndFalseAfterSkipOrFinish()
    {
        var skipped = FlowAt(2);
        Assert.True(skipped.IsApplyBlocked);
        skipped.Skip();
        Assert.False(skipped.IsApplyBlocked);

        var finished = FlowAt(4);
        Assert.True(finished.IsApplyBlocked);
        Assert.True(finished.Next());
        Assert.False(finished.IsActive);
        Assert.False(finished.IsApplyBlocked);
    }

    [Fact]
    public void Start_WhileActive_RestartsFromFirstStep_WithoutEndingTheTour()
    {
        var flow = FlowAt(3);
        var endedCount = 0;
        flow.Ended += (_, _) => endedCount++;

        flow.Start(hasCombo: true);

        Assert.True(flow.IsActive);
        Assert.Equal(1, flow.StepNumber);
        Assert.Equal("character", flow.Current?.Id);
        Assert.Equal(0, endedCount);
    }

    [Fact]
    public void NextAndSkip_WhenNotActive_DoNothing()
    {
        var flow = new TourFlow();
        var endedCount = 0;
        flow.Ended += (_, _) => endedCount++;

        Assert.False(flow.Next());
        flow.Skip();
        flow.Finish();

        Assert.False(flow.IsActive);
        Assert.Null(flow.Current);
        Assert.Equal(0, flow.StepNumber);
        Assert.Equal(0, endedCount);
    }
}
