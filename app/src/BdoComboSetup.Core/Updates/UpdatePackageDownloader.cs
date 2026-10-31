using System.Security.Cryptography;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Updates;

/// <summary>
/// Downloads the installer of a release into a temporary folder and verifies it. The installer is only returned when its
/// SHA-256 equals the one the release publishes in SHA256SUMS.txt: a release without a checksum line for the installer is
/// refused before anything is downloaded, and a mismatch deletes the file. A cancelled or failed download leaves no file.
/// </summary>
public sealed class UpdatePackageDownloader(IUpdateHttpClient http, AppLogger logger, string workDirectory)
{
    private const int BufferSize = 81920;
    private const string PartialSuffix = ".partial";

    private readonly IUpdateHttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _workDirectory = !string.IsNullOrWhiteSpace(workDirectory)
        ? workDirectory
        : throw new ArgumentException("The work folder is required.", nameof(workDirectory));

    /// <summary>The temporary folder of the updater: %TEMP%\BdoComboSetup\updates.</summary>
    public static string DefaultWorkDirectory =>
        Path.Combine(Path.GetTempPath(), "BDOTempo", "updates");

    /// <summary>Downloads and verifies the installer. <paramref name="progress"/> reports a fraction from 0 to 1 when the size is known.</summary>
    public async Task<UpdatePackage> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        using var scope = _logger.BeginOperation("update.download", new Dictionary<string, object?>
        {
            ["version"] = release.Version.ToString(),
        });

        try
        {
            var installer = ReleaseParser.FindInstaller(release)
                ?? throw new UpdateException(UpdateErrorCode.AssetMissing, "The release has no installer asset.");
            var checksumAsset = ReleaseParser.FindChecksums(release)
                ?? throw new UpdateException(UpdateErrorCode.AssetMissing, "The release has no checksum file.");

            var expected = await ReadExpectedHashAsync(checksumAsset, installer.Name, cancellationToken).ConfigureAwait(true);
            _logger.Info("update.checksum.read", "The installer's SHA-256 is published by the release");

            Directory.CreateDirectory(_workDirectory);
            CleanWorkDirectory();

            var target = Path.Combine(_workDirectory, installer.Name);
            var partial = target + PartialSuffix;
            try
            {
                var (actual, length) = await StreamToFileAsync(installer, partial, progress, cancellationToken).ConfigureAwait(true);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error("update.checksum.mismatch", "The downloaded installer does not match the published SHA-256; it was deleted",
                        new InvalidDataException("SHA-256 mismatch"), new Dictionary<string, object?> { ["file"] = installer.Name });
                    throw new UpdateException(UpdateErrorCode.ChecksumMismatch, "The installer's SHA-256 does not match the release.");
                }

                File.Move(partial, target, overwrite: true);
                _logger.Info("update.checksum.verified", "Installer verified against the published SHA-256", new Dictionary<string, object?>
                {
                    ["file"] = installer.Name,
                    ["bytes"] = length,
                });

                var package = new UpdatePackage(target, release.Version, actual, length);
                return package;
            }
            catch
            {
                DeleteQuietly(partial);
                throw;
            }
        }
        catch (UpdateException ex)
        {
            scope.Fail(ex);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("update.download.cancelled", "Download cancelled by the user");
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            var mapped = new UpdateException(UpdateErrorCode.DownloadFailed, "The installer could not be downloaded.", ex);
            scope.Fail(mapped);
            throw mapped;
        }
    }

    /// <summary>Reads the checksum file (small, size-capped) and returns the line for the installer.</summary>
    private async Task<string> ReadExpectedHashAsync(ReleaseAsset checksumAsset, string installerName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, checksumAsset.DownloadUrl);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(true);
        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateException(UpdateErrorCode.DownloadFailed, $"The checksum file answered {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength > UpdateRepository.MaxChecksumBytes)
        {
            throw new UpdateException(UpdateErrorCode.ChecksumMissing, "The checksum file is too large to be trusted.");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
        if (text.Length > UpdateRepository.MaxChecksumBytes)
        {
            throw new UpdateException(UpdateErrorCode.ChecksumMissing, "The checksum file is too large to be trusted.");
        }

        var hash = ChecksumFile.Parse(text).Find(installerName)
            ?? throw new UpdateException(UpdateErrorCode.ChecksumMissing, "The checksum file has no line for the installer.");
        return hash;
    }

    /// <summary>Streams the installer to a partial file while hashing it. Returns the hash (lowercase hex) and the length.</summary>
    private async Task<(string Hash, long Length)> StreamToFileAsync(
        ReleaseAsset installer,
        string partialPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, installer.DownloadUrl);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(true);
        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateException(UpdateErrorCode.DownloadFailed, $"The installer answered {(int)response.StatusCode}.");
        }

        var total = response.Content.Headers.ContentLength ?? (installer.Size > 0 ? installer.Size : -1L);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
        await using var destination = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(true)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);
            hasher.AppendData(buffer, 0, read);
            written += read;
            if (total > 0)
            {
                progress?.Report(Math.Min(1d, (double)written / total));
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(true);
        if (total > 0 && written != total)
        {
            throw new UpdateException(UpdateErrorCode.DownloadFailed, "The installer download was truncated.");
        }

        var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return (hash, written);
    }

    /// <summary>Removes the partial and older installers of this folder. Only files of the updater are touched.</summary>
    private void CleanWorkDirectory()
    {
        foreach (var file in Directory.EnumerateFiles(_workDirectory))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith(UpdateRepository.InstallerPrefix, StringComparison.Ordinal))
            {
                DeleteQuietly(file);
            }
        }
    }

    private void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("update.cleanup.failed", "A temporary installer could not be deleted", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
        }
    }
}
