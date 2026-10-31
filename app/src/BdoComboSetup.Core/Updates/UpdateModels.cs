namespace BdoComboSetup.Core.Updates;

/// <summary>Where the updater looks: the GitHub repository of the app. The only place that names the owner and the repository.</summary>
public static class UpdateRepository
{
    public const string Owner = "TitoSaavedra";
    public const string Name = "bdotempo";

    /// <summary>The "latest release" endpoint. It never returns drafts or pre-releases.</summary>
    public static Uri LatestReleaseApi { get; } = new($"https://api.github.com/repos/{Owner}/{Name}/releases/latest");

    /// <summary>The public release page of the repository (the portable build opens a release from here).</summary>
    public static Uri ReleasesPage { get; } = new($"https://github.com/{Owner}/{Name}/releases");

    /// <summary>The installer asset: <c>BDOTempo-Setup-&lt;version&gt;.exe</c>.</summary>
    public const string InstallerPrefix = "BDOTempo-Setup-";

    public const string InstallerSuffix = ".exe";

    /// <summary>The checksum asset: the SHA-256 of every release asset, in the <c>sha256sum</c> format.</summary>
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    /// <summary>Written only into the portable zip, next to the exe. Its presence means "portable mode".</summary>
    public const string PortableMarkerFileName = "portable.flag";

    /// <summary>An automatic check at startup runs at most once in this interval.</summary>
    public static TimeSpan AutomaticCheckInterval { get; } = TimeSpan.FromHours(6);

    /// <summary>Timeout of the release lookup. The download itself has no time limit; it can be cancelled.</summary>
    public static TimeSpan LookupTimeout { get; } = TimeSpan.FromSeconds(15);

    /// <summary>The checksum file is tiny; a larger answer is refused.</summary>
    public const int MaxChecksumBytes = 64 * 1024;
}

/// <summary>One file of a GitHub release.</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

/// <summary>The latest published release, as the updater needs it.</summary>
public sealed record ReleaseInfo(
    string TagName,
    SemanticVersion Version,
    string Title,
    string Notes,
    Uri PageUrl,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Why an update step failed. The UI maps each code to a text; the log keeps the code.</summary>
public enum UpdateErrorCode
{
    /// <summary>The release lookup answered 404: no release, or the repository is not visible.</summary>
    NotFound,

    /// <summary>GitHub refused the request because of its rate limit (HTTP 403/429 with the limit reached).</summary>
    RateLimited,

    /// <summary>No network, or the request timed out.</summary>
    Offline,

    /// <summary>Any other HTTP failure of the lookup (for example a 5xx answer).</summary>
    Unavailable,

    /// <summary>The release JSON is not what the updater expects (no valid tag, no https URLs).</summary>
    InvalidRelease,

    /// <summary>The release has no installer asset, or no checksum file.</summary>
    AssetMissing,

    /// <summary>The checksum file has no line for the installer. The installer is never run without its hash.</summary>
    ChecksumMissing,

    /// <summary>The downloaded installer does not have the SHA-256 published in the release. It is deleted.</summary>
    ChecksumMismatch,

    /// <summary>The download failed or was truncated.</summary>
    DownloadFailed,

    /// <summary>Windows did not start the installer.</summary>
    InstallerLaunchFailed,
}

/// <summary>An update step failed with a code the UI can explain.</summary>
public sealed class UpdateException(UpdateErrorCode code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public UpdateErrorCode Code { get; } = code;
}

/// <summary>The outcome of one check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>No check ran: the automatic check is off, or the last one was less than six hours ago.</summary>
    Skipped,

    /// <summary>The running version is the latest, or newer.</summary>
    UpToDate,

    /// <summary>A newer release exists.</summary>
    Available,

    /// <summary>The check ran and failed; <see cref="UpdateCheckResult.Error"/> says why.</summary>
    Failed,
}

/// <summary>Why a check runs: at startup (automatic, throttled) or from the Settings button (always runs).</summary>
public enum UpdateTrigger
{
    Startup,
    Manual,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    ReleaseInfo? Release,
    UpdateErrorCode? Error,
    DateTimeOffset? CheckedAt);

/// <summary>An installer downloaded to the temporary folder and verified against the published SHA-256.</summary>
public sealed record UpdatePackage(string FilePath, SemanticVersion Version, string Sha256, long Length);

/// <summary>Portable mode (the marker file next to the exe) or the installed copy.</summary>
public enum InstallKind
{
    Installed,
    Portable,
}
