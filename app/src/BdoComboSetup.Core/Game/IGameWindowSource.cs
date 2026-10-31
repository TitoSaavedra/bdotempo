namespace BdoComboSetup.Core.Game;

public enum GameWindowStatus
{
    NotConnected,
    NotRunning,
    Running,
}

/// <summary>
/// Source of the BlackDesert64.exe window for the real-time preview.
/// Only the contract exists for now; the capture implementation (Windows.Graphics.Capture) comes later.
/// </summary>
public interface IGameWindowSource
{
    GameWindowStatus GetStatus();
}

/// <summary>Placeholder until the capture is implemented. Never captures anything.</summary>
public sealed class StubGameWindowSource : IGameWindowSource
{
    public GameWindowStatus GetStatus() => GameWindowStatus.NotConnected;
}
