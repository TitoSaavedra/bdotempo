using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Tests;

/// <summary>The live background: which state the source is in, and when the capture starts and stops. Fakes stand in for the window and the capture.</summary>
public sealed class GameScreenSourceTests : IDisposable
{
    private const nint Game = 100;
    private const nint OtherGame = 200;

    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private static GameWindowInfo Visible(nint handle = Game) => new(handle, IsMinimized: false);

    private static GameWindowInfo Minimized(nint handle = Game) => new(handle, IsMinimized: true);

    private static GameScreenFrame Frame(byte value) => new(2, 1, [value, value, value, 255, value, value, value, 255]);

    private (GameScreenSource Source, FakeWindowLocator Locator, FakeFrameCapture Capture) Create()
    {
        var locator = new FakeWindowLocator();
        var capture = new FakeFrameCapture();
        return (new GameScreenSource(locator, capture, _log.Logger), locator, capture);
    }

    [Fact]
    public void Without_a_window_the_background_is_black_and_nothing_is_captured()
    {
        var (source, _, capture) = Create();

        source.Poll();

        Assert.Equal(GameScreenState.NotRunning, source.State);
        Assert.Null(source.LatestFrame);
        Assert.Equal(0, capture.StartCalls);
    }

    [Fact]
    public void A_visible_window_is_captured_once_and_polling_again_does_not_restart_it()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();

        source.Poll();
        source.Poll();
        source.Poll();

        Assert.Equal(GameScreenState.Capturing, source.State);
        Assert.Equal(1, capture.StartCalls);
        Assert.Equal(Game, capture.Started);
    }

    [Fact]
    public void Delivered_frames_become_the_latest_frame_and_raise_frame_arrived()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        source.Poll();
        var arrived = 0;
        source.FrameArrived += (_, _) => arrived++;

        var frame = Frame(7);
        capture.Emit(frame);

        Assert.Same(frame, source.LatestFrame);
        Assert.Equal(1, source.FrameVersion);
        Assert.Equal(1, arrived);
    }

    [Fact]
    public void A_minimized_window_goes_black_and_stops_the_capture()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        source.Poll();
        capture.Emit(Frame(1));

        locator.Window = Minimized();
        source.Poll();

        Assert.Equal(GameScreenState.Minimized, source.State);
        Assert.Null(source.LatestFrame);
        Assert.Equal(1, capture.StopCalls);
    }

    [Fact]
    public void A_frame_that_arrives_after_the_minimize_is_never_shown()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        source.Poll();
        var late = capture.OnFrame!;
        locator.Window = Minimized();
        source.Poll();

        late(Frame(9));

        Assert.Null(source.LatestFrame);
        Assert.Equal(0, source.FrameVersion);
    }

    [Fact]
    public void A_minimized_window_that_is_restored_is_captured_again_and_the_old_frame_is_not_shown()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        source.Poll();
        capture.Emit(Frame(1));
        locator.Window = Minimized();
        source.Poll();

        locator.Window = Visible();
        source.Poll();

        Assert.Equal(GameScreenState.Capturing, source.State);
        Assert.Equal(2, capture.StartCalls);
        Assert.Null(source.LatestFrame);
    }

    [Fact]
    public void A_window_that_starts_minimized_stays_black_and_is_not_captured()
    {
        var (source, locator, capture) = Create();
        locator.Window = Minimized();

        source.Poll();

        Assert.Equal(GameScreenState.Minimized, source.State);
        Assert.Equal(0, capture.StartCalls);
    }

    [Fact]
    public void A_closed_window_goes_black_and_a_reopened_one_is_captured_again()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        source.Poll();

        locator.Window = null;
        source.Poll();
        Assert.Equal(GameScreenState.NotRunning, source.State);
        Assert.Equal(1, capture.StopCalls);

        locator.Window = Visible();
        source.Poll();
        Assert.Equal(GameScreenState.Capturing, source.State);
        Assert.Equal(2, capture.StartCalls);
    }

    [Fact]
    public void A_capture_that_cannot_start_is_black_and_is_not_retried_on_every_poll()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        capture.FailNextStart = true;

        source.Poll();
        source.Poll();
        source.Poll();

        Assert.Equal(GameScreenState.Failed, source.State);
        Assert.Null(source.LatestFrame);
        Assert.Equal(1, capture.StartCalls);
    }

    [Fact]
    public void A_failed_capture_is_tried_again_after_the_window_is_minimized_and_restored()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        capture.FailNextStart = true;
        source.Poll();

        locator.Window = Minimized();
        source.Poll();
        locator.Window = Visible();
        source.Poll();

        Assert.Equal(GameScreenState.Capturing, source.State);
        Assert.Equal(2, capture.StartCalls);
    }

    [Fact]
    public void A_capture_that_ends_by_itself_goes_black_and_starts_again_on_the_next_poll()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        source.Poll();

        capture.End("window_closed");

        Assert.Equal(GameScreenState.NotRunning, source.State);
        Assert.Null(source.LatestFrame);

        source.Poll();
        Assert.Equal(GameScreenState.Capturing, source.State);
        Assert.Equal(2, capture.StartCalls);
    }

    [Fact]
    public void A_different_window_handle_replaces_the_capture()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible(Game);
        source.Poll();

        locator.Window = Visible(OtherGame);
        source.Poll();

        Assert.Equal(GameScreenState.Capturing, source.State);
        Assert.Equal(1, capture.StopCalls);
        Assert.Equal(2, capture.StartCalls);
        Assert.Equal(OtherGame, capture.Started);
    }

    [Fact]
    public void Stop_goes_black_and_a_later_poll_captures_again()
    {
        var (source, locator, capture) = Create();
        locator.Window = Visible();
        source.Poll();

        source.Stop();

        Assert.Equal(GameScreenState.NotRunning, source.State);
        Assert.Null(source.LatestFrame);
        Assert.Equal(1, capture.StopCalls);

        source.Poll();
        Assert.Equal(GameScreenState.Capturing, source.State);
    }

    [Fact]
    public void The_black_source_never_captures_and_never_raises_frames()
    {
        var source = new BlackGameScreenSource();
        var arrived = 0;
        source.FrameArrived += (_, _) => arrived++;

        source.Poll();

        Assert.Equal(GameScreenState.NotRunning, source.State);
        Assert.Null(source.LatestFrame);
        Assert.Equal(0, arrived);
    }

    /// <summary>Answers the window the test sets. Nothing else.</summary>
    private sealed class FakeWindowLocator : IGameWindowLocator
    {
        public GameWindowInfo? Window { get; set; }

        public GameWindowInfo? Find() => Window;
    }

    /// <summary>Records the calls and lets the test deliver frames or end the capture.</summary>
    private sealed class FakeFrameCapture : IGameFrameCapture
    {
        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public nint? Started { get; private set; }

        public bool FailNextStart { get; set; }

        public Action<GameScreenFrame>? OnFrame { get; private set; }

        public Action<string>? OnEnded { get; private set; }

        public void Start(nint window, Action<GameScreenFrame> onFrame, Action<string> onEnded)
        {
            StartCalls++;
            if (FailNextStart)
            {
                FailNextStart = false;
                throw new InvalidOperationException("injected: the window cannot be captured");
            }

            Started = window;
            OnFrame = onFrame;
            OnEnded = onEnded;
        }

        public void Stop()
        {
            StopCalls++;
            Started = null;
            OnFrame = null;
            OnEnded = null;
        }

        public void Dispose()
        {
        }

        public void Emit(GameScreenFrame frame) => OnFrame?.Invoke(frame);

        public void End(string reason) => OnEnded?.Invoke(reason);
    }
}
