using System.ComponentModel;
using System.Windows.Input;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Tour;
using Microsoft.UI.Xaml.Input;

namespace BdoComboSetup.Services;

/// <summary>
/// The guided tour of the app. The rules (steps, the wait for a drop, the Apply block) are in <see cref="TourFlow"/>; this
/// class only logs them and tells the main window to draw the tip. It notifies the bindings of the Editor page (the Apply
/// button is disabled while the tour is active).
/// </summary>
public sealed class TourService : INotifyPropertyChanged
{
    private readonly TourFlow _flow = new();
    private readonly AppLogger _logger;

    public TourService(AppLogger logger)
    {
        _logger = logger;
        _flow.StepChanged += (_, _) => OnFlowChanged();
        _flow.Ended += (_, e) => OnEnded(e.Reason);
    }

    /// <summary>Raised when the current step, its action or its availability changes: the window redraws the tip.</summary>
    public event EventHandler? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsActive => _flow.IsActive;

    /// <summary>True while the tour is active: the Apply write does nothing.</summary>
    public bool IsApplyBlocked => _flow.IsApplyBlocked;

    public TourStep? Current => _flow.Current;

    public int StepNumber => _flow.StepNumber;

    public int StepCount => _flow.StepCount;

    public bool IsLastStep => _flow.IsLastStep;

    public bool IsWaitingForAction => _flow.IsWaitingForAction;

    public bool CanAdvance => _flow.CanAdvance;

    /// <summary>Starts the tour at its first step; starting while it is active restarts it.</summary>
    public void Start(bool hasCombo)
    {
        var restarted = _flow.IsActive;
        _flow.Start(hasCombo);
        _logger.Info("tour.started", "Guided tour started", new Dictionary<string, object?>
        {
            ["restarted"] = restarted,
            ["has_combo"] = hasCombo,
            ["steps"] = _flow.StepCount,
        });
    }

    /// <summary>Tells the tour whether a combo is loaded now (the drag step waits only with a combo).</summary>
    public void UpdateCombo(bool hasCombo) => _flow.UpdateCombo(hasCombo);

    /// <summary>The user did the step's action: a skill was dropped on a board cell.</summary>
    public void CompleteAction()
    {
        if (!_flow.IsWaitingForAction)
        {
            return;
        }

        _logger.Info("tour.action_done", "Guided tour step action done", new Dictionary<string, object?>
        {
            ["step"] = _flow.StepNumber,
            ["step_id"] = _flow.Current?.Id,
        });
        _flow.CompleteAction();
    }

    /// <summary>Goes to the next step, or finishes the tour on the last one. False when it is refused.</summary>
    public bool Next()
    {
        var moved = _flow.Next();
        if (!moved && _flow.IsActive)
        {
            _logger.Debug("tour.next_refused", "Guided tour Next refused: the step waits for the user", new Dictionary<string, object?>
            {
                ["step"] = _flow.StepNumber,
                ["step_id"] = _flow.Current?.Id,
            });
        }

        return moved;
    }

    /// <summary>Skips the current step (the "Skip" button of the tip); on the last step the tour finishes.</summary>
    public void SkipStep()
    {
        _logger.Info("tour.step_skipped", "Guided tour step skipped", new Dictionary<string, object?>
        {
            ["step"] = _flow.StepNumber,
            ["step_id"] = _flow.Current?.Id,
        });
        _flow.SkipStep();
    }

    /// <summary>Ends the whole tour before its last step.</summary>
    public void Skip() => _flow.Skip();

    private void OnFlowChanged()
    {
        if (_flow.Current is { } step)
        {
            _logger.Info("tour.step", "Guided tour step shown", new Dictionary<string, object?>
            {
                ["step"] = _flow.StepNumber,
                ["step_id"] = step.Id,
                ["waits_for_action"] = _flow.IsWaitingForAction,
            });
        }

        Notify();
    }

    private void OnEnded(TourEndReason reason)
    {
        _logger.Info(
            reason == TourEndReason.Finished ? "tour.finished" : "tour.skipped",
            reason == TourEndReason.Finished ? "Guided tour finished" : "Guided tour skipped",
            new Dictionary<string, object?> { ["reason"] = reason.ToString() });
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsApplyBlocked)));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A command of the tip's action button: runs an action and follows the availability of the step.</summary>
internal sealed class TourCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
