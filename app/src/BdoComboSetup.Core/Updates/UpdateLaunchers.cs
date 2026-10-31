using System.ComponentModel;
using System.Diagnostics;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Updates;

/// <summary>Starts the verified installer. The app exits right after a successful start (the caller does that).</summary>
public interface IInstallerLauncher
{
    /// <summary>Starts the installer with the given arguments. Throws <see cref="UpdateException"/> when Windows refuses.</summary>
    void Launch(string installerPath, string arguments);
}

/// <summary>Opens a web address in the default browser. Only https addresses of GitHub are opened.</summary>
public interface IUrlOpener
{
    void Open(Uri uri);
}

/// <summary>
/// The installer arguments. <c>/VERYSILENT</c> shows no wizard; <c>/CLOSEAPPLICATIONS</c> closes a running copy of the app
/// (the Inno Setup "close applications" check); <c>/RESTARTAPPLICATIONS</c> starts the closed applications again;
/// <c>/LOG</c> keeps the installer log next to the app logs, so a failed silent update can be read.
/// </summary>
public static class InstallerArguments
{
    public static string ForSilentUpdate(string logPath) =>
        $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /LOG=\"{logPath}\"";
}

/// <summary>Starts the installer with <see cref="Process.Start(ProcessStartInfo)"/>, without a shell.</summary>
public sealed class ProcessInstallerLauncher(AppLogger logger) : IInstallerLauncher
{
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Launch(string installerPath, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        using var scope = _logger.BeginOperation("update.install.launch", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(installerPath),
        });

        try
        {
            using var process = Process.Start(new ProcessStartInfo(installerPath, arguments) { UseShellExecute = false });
            if (process is null)
            {
                throw new UpdateException(UpdateErrorCode.InstallerLaunchFailed, "Windows did not start the installer.");
            }

            _logger.Info("update.install.started", "Installer started; the app closes now", new Dictionary<string, object?>
            {
                ["process_id"] = process.Id,
            });
        }
        catch (UpdateException ex)
        {
            scope.Fail(ex);
            throw;
        }
        catch (Win32Exception ex)
        {
            var mapped = new UpdateException(UpdateErrorCode.InstallerLaunchFailed, "Windows did not start the installer.", ex);
            scope.Fail(mapped);
            throw mapped;
        }
    }
}

/// <summary>Opens the default browser. Refuses any address that is not https on github.com.</summary>
public sealed class ShellUrlOpener(AppLogger logger) : IUrlOpener
{
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warn("update.open.refused", "Address refused: only https links to github.com are opened");
            throw new InvalidOperationException("Only https links to github.com are opened.");
        }

        _logger.Info("update.open.page", "Opening the release page in the browser");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
