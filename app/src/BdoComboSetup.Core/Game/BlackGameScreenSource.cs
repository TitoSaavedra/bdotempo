namespace BdoComboSetup.Core.Game;

/// <summary>
/// A background that is always black and never captures anything. The demo mode and the screenshot script use it,
/// so the images never show the game.
/// </summary>
public sealed class BlackGameScreenSource : IGameScreenSource
{
    public GameScreenState State => GameScreenState.NotRunning;

    public GameScreenFrame? LatestFrame => null;

    public long FrameVersion => 0;

    /// <summary>Never raised: nothing is ever captured.</summary>
    public event EventHandler? FrameArrived
    {
        add { }
        remove { }
    }

    public void Poll()
    {
    }

    public void Stop()
    {
    }
}
