using System.ComponentModel;
using System.Diagnostics;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Game;

/// <summary>Answers whether the game client is running.</summary>
public interface IGameProcessGuard
{
    bool IsGameRunning();
}

/// <summary>A file the guard checks before a write.</summary>
public sealed record GuardTarget(string Pcid, string FilePath, DateTime LastWriteUtc);

/// <summary>Checks BlackDesert64.exe through the process list (Windows). Assumes "running" when the query fails.</summary>
public sealed class GameProcessGuard(AppLogger logger) : IGameProcessGuard
{
    public const string ProcessName = "BlackDesert64";

    public bool IsGameRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.Warn("guard.platform", "Game process check only works on Windows; assuming the game is not running");
            return false;
        }

        try
        {
            var processes = Process.GetProcessesByName(ProcessName);
            try
            {
                var running = processes.Length > 0;
                logger.Info("guard.process_check", "Game process check", new Dictionary<string, object?> { ["running"] = running });
                return running;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.Error("guard.process_check_failed", "Could not query running processes; assuming the game is running", ex);
            return true;
        }
    }
}

/// <summary>What the guard saw before a write: whether the game runs and whether a target changed recently.</summary>
public sealed record GameWriteDecision(bool GameRunning, bool RecentWrite);

/// <summary>The write safety rule of the restore: refuse while the game runs and a target changed in the last 10 seconds.</summary>
public static class WriteGuard
{
    public static readonly TimeSpan RecentWriteWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The apply flow records what the guard sees and writes anyway. Logs whether the game runs and whether a target was
    /// written in the last <see cref="RecentWriteWindow"/>; nothing is refused.
    /// </summary>
    public static GameWriteDecision Record(IGameProcessGuard guard, IReadOnlyList<GuardTarget> targets, DateTime utcNow, AppLogger logger)
    {
        var recent = targets.Any(target => utcNow - target.LastWriteUtc <= RecentWriteWindow);
        var running = guard.IsGameRunning();
        var decision = new GameWriteDecision(running, recent);
        logger.Info("write.game_decision", "Game state recorded before the write; the write continues", new Dictionary<string, object?>
        {
            ["game_running"] = running,
            ["recent_write"] = recent,
            ["proceeding"] = true,
        });
        return decision;
    }

    /// <summary>Throws <see cref="OperationRefusedException"/> (GameRunning) unless <paramref name="force"/> is set. Used by the restore only.</summary>
    public static void Check(IGameProcessGuard guard, IReadOnlyList<GuardTarget> targets, bool force, DateTime utcNow, AppLogger logger)
    {
        bool? running = null;
        foreach (var target in targets)
        {
            var age = utcNow - target.LastWriteUtc;
            if (age > RecentWriteWindow)
            {
                continue;
            }

            running ??= guard.IsGameRunning();
            if (running != true)
            {
                continue;
            }

            if (!force)
            {
                throw new OperationRefusedException(
                    OperationProblem.GameRunning,
                    $"a character file was modified {Math.Round(age.TotalSeconds):0}s ago while {GameProcessGuard.ProcessName}.exe is running; " +
                    "close the game or force the write");
            }

            logger.Warn("guard.forced", "Writing while the game runs (forced)", new Dictionary<string, object?> { ["pcid"] = target.Pcid });
        }
    }
}
