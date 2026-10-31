using System.Globalization;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// The update state of the app: the Settings page controls and the banner of the main window.
/// <para>
/// Installing is never automatic: the user presses the button. The installed copy downloads and verifies the installer, starts
/// it silently and closes the app. The portable copy opens the release page in the browser instead.
/// </para>
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly IUpdateStateStore _state;
    private readonly IUrlOpener _urls;
    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private readonly CultureInfo _culture;
    private readonly Action _exitApplication;
    private readonly SemanticVersion _currentVersion;
    private CancellationTokenSource? _downloadCancellation;
    private string? _dismissedVersion;

    public UpdateViewModel(
        IUpdateService updates,
        IUpdateStateStore state,
        InstallKind installKind,
        IUrlOpener urls,
        ILocalizer localizer,
        AppLogger logger,
        CultureInfo culture,
        SemanticVersion currentVersion,
        Action exitApplication)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _exitApplication = exitApplication ?? throw new ArgumentNullException(nameof(exitApplication));
        Kind = installKind;

        _checkOnStartup = _state.CheckOnStartup;
        _lastCheckedUtc = _state.LastCheckedUtc;
        if (_state.LastSeenVersion is { } seen)
        {
            _statusMessage = _localizer.Format("Update.Status.LastSeen", seen);
        }
        else
        {
            _statusMessage = string.Empty;
        }
    }

    /// <summary>Installed copy or portable zip (the marker file next to the exe).</summary>
    public InstallKind Kind { get; }

    public bool IsPortable => Kind == InstallKind.Portable;

    public string CurrentVersionLine => _localizer.Format("Update.CurrentVersion", _currentVersion.ToString());

    /// <summary>The Settings line with the last check time, or "never" on this PC.</summary>
    public string LastCheckLine => LastCheckedUtc is { } checkedAt
        ? _localizer.Format("Update.LastCheck", checkedAt.ToLocalTime().ToString("g", _culture))
        : _localizer.Get("Update.LastCheckNever");

    /// <summary>The release found by the last check, or null when there is none newer than the running version.</summary>
    [ObservableProperty]
    private ReleaseInfo? _release;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>Download progress, 0 to 100.</summary>
    [ObservableProperty]
    private double _downloadPercent;

    [ObservableProperty]
    private DateTimeOffset? _lastCheckedUtc;

    [ObservableProperty]
    private bool _checkOnStartup;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Information;

    /// <summary>True while the banner of the main window is shown (a release was found and was not dismissed).</summary>
    [ObservableProperty]
    private bool _isBannerOpen;

    /// <summary>True when there is a release to install or to open.</summary>
    public bool HasRelease => Release is not null;

    public string ReleaseVersionText => Release is { } release ? release.Version.ToString() : string.Empty;

    /// <summary>The release notes as published (the dialog of the banner shows them in full).</summary>
    public string ReleaseNotes => Release?.Notes.Trim() ?? string.Empty;

    public string BannerTitle => Release is { } release
        ? _localizer.Format("Update.Banner.Title", release.Version.ToString())
        : string.Empty;

    public string BannerMessage => IsDownloading
        ? _localizer.Format("Update.Banner.Downloading", Math.Round(DownloadPercent).ToString("0", _culture))
        : _localizer.Get(IsPortable ? "Update.Banner.PortableHint" : "Update.Banner.Hint");

    /// <summary>The label of the install button: "Install now" for the installed copy, "Open the release page" for the portable copy.</summary>
    public string InstallActionText => _localizer.Get(IsPortable ? "Update.Action.OpenPage" : "Update.Action.InstallNow");

    public bool CanCancelDownload => IsDownloading;

    /// <summary>Starts the automatic check at startup. It returns at once to the caller's await; the result arrives later.</summary>
    public Task CheckOnStartupAsync() => RunCheckAsync(UpdateTrigger.Startup);

    /// <summary>Sets the switch "check at startup" and saves it.</summary>
    public void SetCheckOnStartup(bool enabled)
    {
        _state.SetCheckOnStartup(enabled);
        CheckOnStartup = enabled;
        _logger.Info("update.setting.saved", "Automatic update check setting saved", new Dictionary<string, object?> { ["enabled"] = enabled });
    }

    /// <summary>Hides the banner until the next check; the same release is not shown again in this session.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        _dismissedVersion = Release?.Version.ToString();
        IsBannerOpen = false;
        _logger.Info("update.banner.dismissed", "Update banner dismissed for this session");
    }

    [RelayCommand]
    private Task CheckNowAsync() => RunCheckAsync(UpdateTrigger.Manual);

    [RelayCommand]
    private void CancelDownload()
    {
        _logger.Info("update.download.cancel_requested", "Download cancel requested by the user");
        _downloadCancellation?.Cancel();
    }

    /// <summary>
    /// The installed copy: downloads and verifies the installer, starts it and closes the app. The portable copy: opens the
    /// release page in the browser. Nothing is installed without this button.
    /// </summary>
    [RelayCommand]
    private async Task InstallAsync()
    {
        var release = Release;
        if (release is null || IsDownloading)
        {
            return;
        }

        if (IsPortable)
        {
            OpenReleasePage(release);
            return;
        }

        _logger.Info("update.install.clicked", "Install button pressed", new Dictionary<string, object?>
        {
            ["version"] = release.Version.ToString(),
        });
        _downloadCancellation = new CancellationTokenSource();
        IsDownloading = true;
        DownloadPercent = 0;
        OnPropertyChanged(nameof(BannerMessage));
        SetStatus(StatusSeverity.Information, "Update.Status.Downloading", release.Version.ToString());
        var progress = new Progress<double>(fraction => DownloadPercent = Math.Clamp(fraction * 100, 0, 100));
        try
        {
            var package = await _updates.DownloadAsync(release, progress, _downloadCancellation.Token).ConfigureAwait(true);
            IsDownloading = false;
            OnPropertyChanged(nameof(BannerMessage));
            _updates.LaunchInstaller(package);
            SetStatus(StatusSeverity.Information, "Update.Status.Installing", release.Version.ToString());
            _exitApplication();
        }
        catch (OperationCanceledException)
        {
            SetStatus(StatusSeverity.Information, "Update.Status.DownloadCancelled");
        }
        catch (UpdateException ex)
        {
            _logger.Warn("update.install.failed", "The update could not be installed", new Dictionary<string, object?>
            {
                ["code"] = ex.Code.ToString(),
            });
            SetStatus(StatusSeverity.Error, ErrorKey(ex.Code));
        }
        catch (Exception ex)
        {
            _logger.Error("update.install.unexpected", "The update failed unexpectedly", ex);
            SetStatus(StatusSeverity.Error, "Update.Error.Download");
        }
        finally
        {
            IsDownloading = false;
            OnPropertyChanged(nameof(BannerMessage));
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    partial void OnReleaseChanged(ReleaseInfo? value)
    {
        OnPropertyChanged(nameof(HasRelease));
        OnPropertyChanged(nameof(ReleaseVersionText));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(BannerTitle));
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelDownload));
        OnPropertyChanged(nameof(BannerMessage));
    }

    partial void OnLastCheckedUtcChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastCheckLine));

    private async Task RunCheckAsync(UpdateTrigger trigger)
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        if (trigger == UpdateTrigger.Manual)
        {
            SetStatus(StatusSeverity.Information, "Update.Status.Checking");
        }

        try
        {
            var result = await _updates.CheckAsync(trigger, CancellationToken.None).ConfigureAwait(true);
            ApplyResult(result, trigger);
        }
        catch (Exception ex)
        {
            _logger.Error("update.check.unexpected", "Update check crashed; the UI shows the check as failed", ex);
            SetStatus(StatusSeverity.Warning, "Update.Status.Failed");
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void ApplyResult(UpdateCheckResult result, UpdateTrigger trigger)
    {
        if (result.CheckedAt is { } checkedAt)
        {
            LastCheckedUtc = checkedAt;
        }

        switch (result.Status)
        {
            case UpdateCheckStatus.Skipped:
                // A throttled or disabled startup check is silent. A manual check is never skipped.
                break;

            case UpdateCheckStatus.UpToDate:
                Release = null;
                IsBannerOpen = false;
                SetStatus(StatusSeverity.Success, "Update.Status.UpToDate", _currentVersion.ToString());
                break;

            case UpdateCheckStatus.Available:
                var release = result.Release!;
                Release = release;
                IsBannerOpen = _dismissedVersion != release.Version.ToString();
                SetStatus(StatusSeverity.Information, "Update.Status.Available", release.Version.ToString());
                break;

            case UpdateCheckStatus.Failed:
                // Keeps a release that was already known: a failed re-check does not hide an update.
                SetStatus(StatusSeverity.Warning, result.Error switch
                {
                    UpdateErrorCode.RateLimited => "Update.Status.RateLimited",
                    UpdateErrorCode.Offline => "Update.Status.Offline",
                    _ => "Update.Status.Failed",
                });
                break;
        }

        _logger.Debug("update.view.applied", "Update result shown", new Dictionary<string, object?>
        {
            ["trigger"] = trigger.ToString().ToLowerInvariant(),
            ["status"] = result.Status.ToString().ToLowerInvariant(),
        });
    }

    private void OpenReleasePage(ReleaseInfo release)
    {
        try
        {
            _urls.Open(release.PageUrl);
            SetStatus(StatusSeverity.Information, "Update.Status.PortableOpened", release.Version.ToString());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("update.open.failed", "The release page could not be opened", ex);
            SetStatus(StatusSeverity.Error, "Update.Error.OpenFailed");
        }
    }

    private static string ErrorKey(UpdateErrorCode code) => code switch
    {
        UpdateErrorCode.AssetMissing => "Update.Error.AssetMissing",
        UpdateErrorCode.ChecksumMissing => "Update.Error.ChecksumMissing",
        UpdateErrorCode.ChecksumMismatch => "Update.Error.ChecksumMismatch",
        UpdateErrorCode.InstallerLaunchFailed => "Update.Error.LaunchFailed",
        _ => "Update.Error.Download",
    };

    private void SetStatus(StatusSeverity severity, string key, params object[] args)
    {
        StatusSeverity = severity;
        StatusMessage = args.Length == 0 ? _localizer.Get(key) : _localizer.Format(key, args);
    }
}
