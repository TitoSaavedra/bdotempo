using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Game;

/// <summary>
/// The live background of the Preview: capture of the visible Black Desert window, black otherwise.
/// Rules: no window or no game process: black. Minimized window: black, and the capture stops (a frozen frame is never
/// shown). Visible window: captured. A window whose capture cannot start is black, logged once, and not retried on every
/// poll (only after the window is minimized and restored, or closed and opened again). A capture whose frames cannot be
/// read is black and retried after <see cref="FrameRetryDelayMs"/>; after <see cref="MaxFrameFailures"/> failures in a
/// row it stays black like a window that cannot start. Nothing here touches the game.
/// </summary>
public sealed class GameScreenSource : IGameScreenSource, IDisposable
{
    /// <summary>Delay before a capture whose frames failed is started again.</summary>
    public const long FrameRetryDelayMs = 5000;

    /// <summary>Frame failures in a row for one window before the background stays black until the window is restored.</summary>
    public const int MaxFrameFailures = 3;

    private const string FrameFailedReason = "frame_failed";

    private readonly IGameWindowLocator _locator;
    private readonly IGameFrameCapture _capture;
    private readonly AppLogger _logger;
    private readonly object _gate = new();
    private GameScreenState _state = GameScreenState.NotRunning;
    private nint? _capturedWindow;
    private nint? _failedWindow;
    private nint? _retryWindow;
    private long _retryNotBeforeMs;
    private int _frameFailures;
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

    /// <summary>Raised when the state changes (from any thread).</summary>
    public event EventHandler? StateChanged;

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
            ForgetFailures();
            Release("game_screen.window_missing", GameScreenState.NotRunning);
            return;
        }

        if (window.IsMinimized)
        {
            // Restoring the window must try again, so the failure memory is cleared with it.
            ForgetFailures();
            Release("game_screen.minimized", GameScreenState.Minimized);
            return;
        }

        bool retryDue;
        lock (_gate)
        {
            if (_capturedWindow == window.Handle || _failedWindow == window.Handle)
            {
                return;
            }

            retryDue = _retryWindow == window.Handle;
            if (retryDue && Environment.TickCount64 < _retryNotBeforeMs)
            {
                // A frame failure is waiting for its retry: the background stays black until the delay has passed.
                return;
            }
        }

        if (!retryDue)
        {
            // Another window than the one being captured (the game was restarted): stop the old capture first.
            ForgetFailures();
            Release("game_screen.window_changed", GameScreenState.NotRunning);
        }

        Start(window.Handle);
    }

    public void Stop()
    {
        ForgetFailures();
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
            lock (_gate)
            {
                _failedWindow = handle;
            }

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

            // A frame arrived: the failures before it no longer count.
            _frameFailures = 0;
            _retryWindow = null;
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

        if (reason == FrameFailedReason)
        {
            FrameFailed(handle);
            return;
        }

        SetState(GameScreenState.NotRunning, "game_screen.capture_ended", "The capture of the game window ended", null, reason);
    }

    /// <summary>
    /// A capture whose frames could not be read has ended. The window is black and retried after the delay. The state
    /// change is logged once; after <see cref="MaxFrameFailures"/> failures in a row the window stays black for good
    /// (until it is minimized and restored, or closed and opened again).
    /// </summary>
    private void FrameFailed(nint handle)
    {
        int attempts;
        bool exhausted;
        lock (_gate)
        {
            _frameFailures = _retryWindow == handle ? _frameFailures + 1 : 1;
            _retryWindow = handle;
            _retryNotBeforeMs = Environment.TickCount64 + FrameRetryDelayMs;
            attempts = _frameFailures;
            exhausted = attempts >= MaxFrameFailures;
            if (exhausted)
            {
                _failedWindow = handle;
            }
        }

        SetState(
            GameScreenState.Failed,
            "game_screen.frame_failed",
            "Frames of the game window could not be read; the background is black",
            null,
            FrameFailedReason);

        if (exhausted)
        {
            _logger.Warn("game_screen.retries_exhausted", "The game window kept failing to deliver frames; the background stays black until it is restored", new Dictionary<string, object?>
            {
                ["attempts"] = attempts,
                ["max_attempts"] = MaxFrameFailures,
            });
        }
    }

    /// <summary>Forgets the failures of earlier tries, so the next poll tries the window again from the start.</summary>
    private void ForgetFailures()
    {
        lock (_gate)
        {
            _failedWindow = null;
            _retryWindow = null;
            _retryNotBeforeMs = 0;
            _frameFailures = 0;
        }
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
}
