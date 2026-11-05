namespace BdoComboSetup.Core.Tour;

/// <summary>
/// One step of the guided tour: the resource keys of its title and text, and whether it waits for the user to do something
/// (the step is passed only when <see cref="TourFlow.CompleteAction"/> is called).
/// </summary>
public sealed record TourStep(string Id, string TitleKey, string TextKey, bool WaitsForAction);

/// <summary>Why the tour ended: the user skipped it, or it went past its last step.</summary>
public enum TourEndReason
{
    Skipped,
    Finished,
}

/// <summary>The reason the tour ended.</summary>
public sealed class TourEndedEventArgs(TourEndReason reason) : EventArgs
{
    public TourEndReason Reason { get; } = reason;
}

/// <summary>
/// The rules of the guided tour: the ordered steps, the current one, and the wait of a step for a user action. It has no UI;
/// the window renders the tip of <see cref="Current"/> and calls into this class. While the tour is active the Apply
/// write is blocked (<see cref="IsApplyBlocked"/>).
/// </summary>
public sealed class TourFlow
{
    /// <summary>The steps of the app's tour, in order.</summary>
    public static IReadOnlyList<TourStep> DefaultSteps { get; } =
    [
        new("character", "Tour.Character.Title", "Tour.Character.Text", WaitsForAction: false),
        new("drag", "Tour.Drag.Title", "Tour.Drag.Text", WaitsForAction: true),
        new("editor", "Tour.Editor.Title", "Tour.Editor.Text", WaitsForAction: false),
        new("editor-settings", "Tour.EditorSettings.Title", "Tour.EditorSettings.Text", WaitsForAction: false),
        new("apply", "Tour.Apply.Title", "Tour.Apply.Text", WaitsForAction: false),
        new("settings", "Tour.Settings.Title", "Tour.Settings.Text", WaitsForAction: false),
    ];

    private readonly IReadOnlyList<TourStep> _steps;

    /// <summary>Index of the current step, or -1 when the tour is not active.</summary>
    private int _index = -1;

    /// <summary>True when the user has done the action of the current step (only a waiting step uses it).</summary>
    private bool _actionDone;

    /// <summary>True when a combo is loaded. A waiting step does not wait without one: there is nothing to drop on.</summary>
    private bool _hasCombo;

    public TourFlow()
        : this(DefaultSteps)
    {
    }

    public TourFlow(IReadOnlyList<TourStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("The tour needs at least one step.", nameof(steps));
        }

        _steps = steps;
    }

    /// <summary>Raised when the current step changes, when the tour starts or restarts, and when the action of a step is done.</summary>
    public event EventHandler? StepChanged;

    /// <summary>Raised once when the tour ends, with the reason.</summary>
    public event EventHandler<TourEndedEventArgs>? Ended;

    public bool IsActive => _index >= 0;

    /// <summary>The Apply write is blocked for as long as the tour is active.</summary>
    public bool IsApplyBlocked => IsActive;

    public int StepCount => _steps.Count;

    /// <summary>The 1-based number of the current step, for the "n / 6" caption; 0 when the tour is not active.</summary>
    public int StepNumber => IsActive ? _index + 1 : 0;

    /// <summary>The current step, or null when the tour is not active.</summary>
    public TourStep? Current => IsActive ? _steps[_index] : null;

    public bool IsLastStep => IsActive && _index == _steps.Count - 1;

    /// <summary>True while the current step waits for the user's action (the drop of a skill on a board cell).</summary>
    public bool IsWaitingForAction => Current is { WaitsForAction: true } && _hasCombo && !_actionDone;

    /// <summary>True when Next is allowed now: the tour is active and the current step is not waiting for an action.</summary>
    public bool CanAdvance => IsActive && !IsWaitingForAction;

    /// <summary>Starts the tour at its first step. Starting while it is active restarts it from the first step; it never ends it.</summary>
    /// <param name="hasCombo">Whether a combo is loaded, which decides whether the drag step waits.</param>
    public void Start(bool hasCombo)
    {
        _hasCombo = hasCombo;
        _index = 0;
        _actionDone = false;
        StepChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tells the tour whether a combo is loaded now (it can load after the tour started).</summary>
    public void UpdateCombo(bool hasCombo)
    {
        var changed = _hasCombo != hasCombo;
        _hasCombo = hasCombo;
        if (changed && IsActive)
        {
            StepChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The user did the action of the current step (a skill was dropped on a board cell). Does nothing when the step does not wait.
    /// </summary>
    public void CompleteAction()
    {
        if (!IsWaitingForAction)
        {
            return;
        }

        _actionDone = true;
        StepChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Goes to the next step. On the last step it finishes the tour. Refused (false) while the tour is not active or the current
    /// step waits for the user's action.
    /// </summary>
    public bool Next()
    {
        if (!CanAdvance)
        {
            return false;
        }

        if (IsLastStep)
        {
            Finish();
            return true;
        }

        _index++;
        _actionDone = false;
        StepChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Skips the current step (the tip's "Skip" button): goes to the next one even when this one waits for an action, and on the
    /// last step it finishes the tour. False when the tour is not active.
    /// </summary>
    public bool SkipStep()
    {
        if (!IsActive)
        {
            return false;
        }

        if (IsLastStep)
        {
            Finish();
            return true;
        }

        _index++;
        _actionDone = false;
        StepChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Ends the whole tour before its last step. Does nothing when it is not active.</summary>
    public void Skip() => End(TourEndReason.Skipped);

    /// <summary>Ends the tour as finished. Does nothing when it is not active.</summary>
    public void Finish() => End(TourEndReason.Finished);

    private void End(TourEndReason reason)
    {
        if (!IsActive)
        {
            return;
        }

        _index = -1;
        _actionDone = false;
        Ended?.Invoke(this, new TourEndedEventArgs(reason));
    }
}
