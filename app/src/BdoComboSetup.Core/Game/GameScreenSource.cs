using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Game;

/// <summary>
/// The live background of the Preview: capture of the visible Black Desert window, black otherwise.
/// Rules: no window or no game process: black. Minimized window: black, and the capture stops (a frozen frame is never
/// shown). Visible window: captured. A window whose capture cannot start is black, logged once, and not retried on every
/// poll (only after the window is minimized and restored, or closed and opened again). Nothing here touches the game.
/// </summary>
public sealed class GameScreenSource : IGameScreenSource, IDisposable
{
    private readonly IGameWindowLocator _locator;
    private readonly IGameFrameCapture _capture;
    private readonly AppLogger _logger;
    private readonly object _gate = new();
    private GameScreenState _state = GameScreenState.NotRunning;
    private nint? _capturedWindow;
    private nint? _failedWindow;
    private GameScreenFrame? _latest;
    private long _frameVersion;

    public GameScreenSource(IGameWindowLocator locator, IGameFrameCapture capture, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(logger);

        _locator = locator;
        _capture = capture;
        _logger = logger;
    }

    /// <summary>Raised on the capture thread for every delivered frame.</summary>
    public event EventHandler? FrameArrived;

    public GameScreenState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public GameScreenFrame? LatestFrame
    {
        get
        {
            lock (_gate)
            {
                return _state == GameScreenState.Capturing ? _latest : null;
            }
        }
    }

    public long FrameVersion
    {
        get
        {
            lock (_gate)
            {
                return _frameVersion;
            }
        }
    }

    public void Poll()
    {
        var window = _locator.Find();
        if (window is null)
        {
            Release("game_screen.window_missing", GameScreenState.NotRunning);
            _failedWindow = null;
            return;
        }

        if (window.IsMinimized)
        {
            // Restoring the window must try again, so the failure memory is cleared with it.
            _failedWindow = null;
            Release("game_screen.minimized", GameScreenState.Minimized);
            return;
        }

        bool capturing;
        lock (_gate)
        {
            capturing = _capturedWindow == window.Handle;
        }

        if (capturing)
        {
            return;
        }

        if (_failedWindow == window.Handle)
        {
            return;
        }

        // Another window than the one being captured (the game was restarted): stop the old capture first.
        Release("game_screen.window_changed", GameScreenState.NotRunning);
        Start(window.Handle);
    }

    public void Stop()
    {
        _failedWindow = null;
        Release("game_screen.stopped", GameScreenState.NotRunning);
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
    }

    private void Start(nint handle)
    {
        try
        {
            _capture.Start(handle, frame => OnFrame(handle, frame), reason => OnEnded(handle, reason));
        }
        catch (Exception ex)
        {
            _failedWindow = handle;
            SetState(GameScreenState.Failed, "game_screen.capture_failed", "The game window could not be captured; the background is black", ex);
            return;
        }

        lock (_gate)
        {
            _capturedWindow = handle;
            _latest = null;
        }

        SetState(GameScreenState.Capturing, "game_screen.capture_started", "Capture of the game window started", null);
    }

    private void OnFrame(nint handle, GameScreenFrame frame)
    {
        lock (_gate)
        {
            if (_capturedWindow != handle)
            {
                return;
            }

            _latest = frame;
            _frameVersion++;
        }

        FrameArrived?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnded(nint handle, string reason)
    {
        lock (_gate)
        {
            if (_capturedWindow != handle)
            {
                return;
            }

            _capturedWindow = null;
            _latest = null;
        }

        SetState(GameScreenState.NotRunning, "game_screen.capture_ended", "The capture of the game window ended", null, reason);
    }

    /// <summary>Stops the capture (when there is one) and sets the state. The capture is stopped outside the lock.</summary>
    private void Release(string logEvent, GameScreenState next)
    {
        bool wasCapturing;
        lock (_gate)
        {
            wasCapturing = _capturedWindow is not null;
            _capturedWindow = null;
            _latest = null;
        }

        if (wasCapturing)
        {
            _capture.Stop();
        }

        SetState(next, logEvent, "Game screen released", null);
    }

    private void SetState(GameScreenState next, string logEvent, string message, Exception? error, string? reason = null)
    {
        GameScreenState previous;
        lock (_gate)
        {
            previous = _state;
            _state = next;
        }

        if (previous == next && error is null)
        {
            return;
        }

        var fields = new Dictionary<string, object?>
        {
            ["state"] = next.ToString(),
            ["previous"] = previous.ToString(),
        };
        if (reason is not null)
        {
            fields["reason"] = reason;
        }

        if (error is not null)
        {
            _logger.Error(logEvent, message, error, fields);
        }
        else
        {
            _logger.Info(logEvent, message, fields);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the state changes (from any thread).</summary>
    public event EventHandler? StateChanged;
}
