using System.ComponentModel;
using System.Diagnostics;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Game;

/// <summary>
/// Looks up whether the game client process exists, for the status bar. Read only: it only reads the process list, it never
/// opens a capture, starts, injects into or writes to the game.
/// </summary>
public static class GameProcessProbe
{
    /// <summary>True when the game process is in the process list; null when the list cannot be read.</summary>
    public static bool? IsGameRunning(AppLogger logger)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(GameProcessGuard.ProcessName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.Warn("game_detection.enumerate_failed", "The process list could not be read; the game state is kept", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
            return null;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

/// <summary>
/// The game detection shown in the status bar. The window polls it every few seconds on the UI thread; it only reports the
/// state and never starts a capture. A change of state is logged once (<c>game.detected</c> or <c>game.not_detected</c>).
/// </summary>
public sealed class GameDetectionMonitor
{
    private readonly Func<bool?> _probe;
    private readonly AppLogger _logger;

    public GameDetectionMonitor(Func<bool?> probe, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(logger);

        _probe = probe;
        _logger = logger;
    }

    /// <summary>True when the game process exists, false when it does not, null before the first readable poll.</summary>
    public bool? IsDetected { get; private set; }

    /// <summary>Raised from <see cref="Poll"/> when <see cref="IsDetected"/> changes.</summary>
    public event EventHandler? DetectionChanged;

    /// <summary>Reads the process list once. An unreadable list keeps the last state.</summary>
    public void Poll()
    {
        var detected = _probe();
        if (detected is null || detected == IsDetected)
        {
            return;
        }

        IsDetected = detected;
        if (detected.Value)
        {
            _logger.Info("game.detected", "Game process detected");
        }
        else
        {
            _logger.Info("game.not_detected", "Game process not detected");
        }

        DetectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
