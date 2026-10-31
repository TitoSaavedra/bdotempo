using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;

namespace BdoComboSetup.Core.Updates;

/// <summary>What the updater needs from the settings: the startup switch and the time of the last check.</summary>
public interface IUpdateStateStore
{
    bool CheckOnStartup { get; }

    DateTimeOffset? LastCheckedUtc { get; }

    string? LastSeenVersion { get; }

    void SetCheckOnStartup(bool enabled);

    /// <summary>Records that a check ran. A null <paramref name="seenVersion"/> keeps the last one seen.</summary>
    void RecordCheck(DateTimeOffset checkedAtUtc, string? seenVersion);
}

/// <summary>Keeps the update state in <see cref="AppSettings"/> (settings.json), like every other setting.</summary>
public sealed class AppSettingsUpdateStateStore(AppSettingsStore settings) : IUpdateStateStore
{
    private readonly AppSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public bool CheckOnStartup => _settings.Settings.CheckUpdatesOnStartup;

    public DateTimeOffset? LastCheckedUtc => _settings.Settings.LastUpdateCheckUtc;

    public string? LastSeenVersion => _settings.Settings.LastSeenVersion;

    public void SetCheckOnStartup(bool enabled) =>
        _settings.Update(current => current with { CheckUpdatesOnStartup = enabled });

    public void RecordCheck(DateTimeOffset checkedAtUtc, string? seenVersion) =>
        _settings.Update(current => current with
        {
            LastUpdateCheckUtc = checkedAtUtc,
            LastSeenVersion = seenVersion ?? current.LastSeenVersion,
        });
}

/// <summary>The updater's use cases: check, download and verify, start the installer.</summary>
public interface IUpdateService
{
    /// <summary>
    /// Checks the latest release. A startup check is skipped when the switch is off or when the last check ran less than six
    /// hours ago; a manual check always runs. Failures are returned as <see cref="UpdateCheckStatus.Failed"/>, never thrown.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(UpdateTrigger trigger, CancellationToken cancellationToken);

    /// <summary>Downloads and verifies the installer of the release (see <see cref="UpdatePackageDownloader"/>).</summary>
    Task<UpdatePackage> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Starts the verified installer silently. The caller then closes the app.</summary>
    void LaunchInstaller(UpdatePackage package);
}

public sealed class UpdateService(
    IReleaseFeed feed,
    UpdatePackageDownloader downloader,
    IInstallerLauncher launcher,
    IUpdateStateStore state,
    TimeProvider clock,
    SemanticVersion currentVersion,
    AppLogger logger,
    string installerLogPath) : IUpdateService
{
    private readonly IReleaseFeed _feed = feed ?? throw new ArgumentNullException(nameof(feed));
    private readonly UpdatePackageDownloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    private readonly IInstallerLauncher _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    private readonly IUpdateStateStore _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly SemanticVersion _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _installerLogPath = installerLogPath ?? throw new ArgumentNullException(nameof(installerLogPath));

    public SemanticVersion CurrentVersion => _currentVersion;

    public async Task<UpdateCheckResult> CheckAsync(UpdateTrigger trigger, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        using var scope = _logger.BeginOperation("update.check", new Dictionary<string, object?>
        {
            ["trigger"] = trigger.ToString().ToLowerInvariant(),
            ["current_version"] = _currentVersion.ToString(),
        });

        if (trigger == UpdateTrigger.Startup)
        {
            if (!_state.CheckOnStartup)
            {
                _logger.Info("update.check.skipped", "Automatic check is off in the settings");
                return new UpdateCheckResult(UpdateCheckStatus.Skipped, null, null, _state.LastCheckedUtc);
            }

            if (IsThrottled(now))
            {
                _logger.Info("update.check.skipped", "Last check was less than six hours ago");
                return new UpdateCheckResult(UpdateCheckStatus.Skipped, null, null, _state.LastCheckedUtc);
            }
        }

        try
        {
            var release = await _feed.GetLatestAsync(cancellationToken).ConfigureAwait(true);
            var newer = release.Version.CompareTo(_currentVersion) > 0;
            _state.RecordCheck(now, release.Version.ToString());
            var status = newer ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate;
            _logger.Info("update.check.result", "Update check finished", new Dictionary<string, object?>
            {
                ["status"] = status.ToString().ToLowerInvariant(),
                ["latest_version"] = release.Version.ToString(),
            });
            return new UpdateCheckResult(status, release, null, now);
        }
        catch (UpdateException ex)
        {
            // The attempt counts for the six-hour rule too: an offline start must not retry on every launch.
            _state.RecordCheck(now, null);
            scope.Fail(ex);
            _logger.Warn("update.check.failed", "Update check failed", new Dictionary<string, object?>
            {
                ["code"] = ex.Code.ToString(),
            });
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, ex.Code, now);
        }
    }

    public Task<UpdatePackage> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken) =>
        _downloader.DownloadAsync(release, progress, cancellationToken);

    public void LaunchInstaller(UpdatePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        _logger.Info("update.install.requested", "Installing the verified update; the app closes after the start", new Dictionary<string, object?>
        {
            ["version"] = package.Version.ToString(),
        });
        _launcher.Launch(package.FilePath, InstallerArguments.ForSilentUpdate(_installerLogPath));
    }

    /// <summary>True when the last check is less than the interval old. A last check in the future (the clock moved) is not throttled.</summary>
    private bool IsThrottled(DateTimeOffset now)
    {
        var last = _state.LastCheckedUtc;
        if (last is null)
        {
            return false;
        }

        var age = now - last.Value;
        return age >= TimeSpan.Zero && age < UpdateRepository.AutomaticCheckInterval;
    }
}
