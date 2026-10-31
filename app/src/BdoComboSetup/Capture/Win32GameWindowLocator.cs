using System.Diagnostics;
using System.Runtime.InteropServices;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Capture;

/// <summary>
/// Finds the main window of BlackDesert64.exe by process name, and whether it is minimized. Read only: it never starts,
/// injects into or writes to the game.
/// </summary>
public sealed partial class Win32GameWindowLocator(AppLogger logger) : IGameWindowLocator
{
    /// <summary>The game's process name, without ".exe".</summary>
    public const string ProcessName = "BlackDesert64";

    public GameWindowInfo? Find()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(ProcessName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.Warn("game_window.enumerate_failed", "The process list could not be read", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
            return null;
        }

        try
        {
            foreach (var process in processes)
            {
                var handle = process.MainWindowHandle;
                if (handle != nint.Zero)
                {
                    return new GameWindowInfo(handle, IsIconic(handle));
                }
            }

            return null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);
}
