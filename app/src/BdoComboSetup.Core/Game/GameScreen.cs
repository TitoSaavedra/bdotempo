namespace BdoComboSetup.Core.Game;

/// <summary>What the Preview background shows right now.</summary>
public enum GameScreenState
{
    /// <summary>No Black Desert window: the background is black.</summary>
    NotRunning,

    /// <summary>The window is minimized: the background is black (a frozen frame is never shown).</summary>
    Minimized,

    /// <summary>The window is captured and its frames are delivered.</summary>
    Capturing,

    /// <summary>The window exists but its capture could not start: the background is black.</summary>
    Failed,
}

/// <summary>One captured frame: BGRA pixels, top row first, stride Width x 4. Never modified after it is delivered.</summary>
public sealed record GameScreenFrame(int Width, int Height, byte[] Bgra);

/// <summary>The Black Desert window as the locator sees it.</summary>
/// <param name="Handle">The window handle (HWND) of the main window.</param>
/// <param name="IsMinimized">True when the window is iconic (minimized). A minimized window is never captured.</param>
public sealed record GameWindowInfo(nint Handle, bool IsMinimized);

/// <summary>Finds the Black Desert main window. Implementations never start, inject into or write to the game.</summary>
public interface IGameWindowLocator
{
    /// <summary>The main window of BlackDesert64.exe, or null when the game is not running or has no window.</summary>
    GameWindowInfo? Find();
}

/// <summary>Captures one window's frames. Implementations are capture only: they read what the window shows.</summary>
public interface IGameFrameCapture : IDisposable
{
    /// <summary>
    /// Starts capturing the window. <paramref name="onFrame"/> is called on the capture thread for each delivered frame.
    /// <paramref name="onEnded"/> is called on the capture thread when the window is closed or the capture stops by itself.
    /// Throws when the capture cannot start.
    /// </summary>
    void Start(nint window, Action<GameScreenFrame> onFrame, Action<string> onEnded);

    /// <summary>Stops the capture. Safe to call when nothing is captured.</summary>
    void Stop();
}

/// <summary>
/// The background of the Preview page. <see cref="Poll"/> is called by a cheap timer on the UI thread; it looks for the
/// window and starts or stops the capture. <see cref="FrameArrived"/> is raised on the capture thread.
/// </summary>
public interface IGameScreenSource
{
    GameScreenState State { get; }

    /// <summary>The newest frame while <see cref="State"/> is Capturing; null in every other state (black).</summary>
    GameScreenFrame? LatestFrame { get; }

    /// <summary>Increases with every delivered frame, so a view can tell a new frame from the last one.</summary>
    long FrameVersion { get; }

    event EventHandler? FrameArrived;

    /// <summary>Looks at the window and starts, keeps or stops the capture.</summary>
    void Poll();

    /// <summary>Stops any capture and goes black. Polling again starts the capture again when the window is visible.</summary>
    void Stop();
}
