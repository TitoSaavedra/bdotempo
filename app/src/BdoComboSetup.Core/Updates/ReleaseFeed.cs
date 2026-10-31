using System.Net;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Updates;

/// <summary>Where the newest release comes from. The GitHub feed is the real one; the demo feed answers from memory.</summary>
public interface IReleaseFeed
{
    /// <summary>The latest release. Throws <see cref="UpdateException"/> with the reason when there is none to read.</summary>
    Task<ReleaseInfo> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads <c>releases/latest</c> of the repository (anonymous, read-only). Every HTTP answer is mapped to an
/// <see cref="UpdateErrorCode"/>: 404 is "not found", 403/429 with the rate limit used up is "rate limited", a network
/// failure or a timeout is "offline", anything else is "unavailable". Nothing is retried here.
/// </summary>
public sealed class GitHubReleaseFeed(IUpdateHttpClient http, AppLogger logger, string userAgent, Uri? endpoint = null) : IReleaseFeed
{
    private readonly IUpdateHttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _userAgent = !string.IsNullOrWhiteSpace(userAgent)
        ? userAgent
        : throw new ArgumentException("The User-Agent is required.", nameof(userAgent));
    private readonly Uri _endpoint = endpoint ?? UpdateRepository.LatestReleaseApi;

    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginOperation("update.release.lookup", new Dictionary<string, object?>
        {
            ["repository"] = $"{UpdateRepository.Owner}/{UpdateRepository.Name}",
        });

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(UpdateRepository.LookupTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.UserAgent.ParseAdd(_userAgent);

            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(true);
            _logger.Debug("update.release.answer", "The release lookup answered", new Dictionary<string, object?>
            {
                ["status"] = (int)response.StatusCode,
            });

            var error = MapFailure(response);
            if (error is not null)
            {
                scope.Fail(error);
                throw error;
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true);
            var release = ReleaseParser.Parse(json);
            _logger.Info("update.release.found", "Latest release read", new Dictionary<string, object?>
            {
                ["version"] = release.Version.ToString(),
                ["assets"] = release.Assets.Count,
            });
            return release;
        }
        catch (UpdateException ex)
        {
            // Already mapped (a failure answer or an invalid release); the operation has logged it.
            scope.Fail(ex);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // A timeout (TaskCanceledException without the caller's cancellation) or a network failure: the app is offline.
            var mapped = new UpdateException(UpdateErrorCode.Offline, "The release lookup could not reach GitHub.", ex);
            scope.Fail(mapped);
            throw mapped;
        }
    }

    private static UpdateException? MapFailure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => new UpdateException(UpdateErrorCode.NotFound, "The repository or its latest release was not found."),
            HttpStatusCode.TooManyRequests => new UpdateException(UpdateErrorCode.RateLimited, "GitHub rate limit reached."),
            HttpStatusCode.Forbidden when IsRateLimited(response) => new UpdateException(UpdateErrorCode.RateLimited, "GitHub rate limit reached."),
            _ => new UpdateException(UpdateErrorCode.Unavailable, $"The release lookup answered {(int)response.StatusCode}."),
        };
    }

    /// <summary>GitHub marks a rate-limit answer with a zero remaining count or a Retry-After header.</summary>
    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
            && remaining.Any(value => value.Trim() == "0"))
        {
            return true;
        }

        return response.Headers.RetryAfter is not null;
    }
}

/// <summary>
/// The feed of the demo mode: no network. It answers "a newer version exists" (for the screenshots of the update banner) or
/// "you have the latest", never anything else.
/// </summary>
public sealed class DemoReleaseFeed(SemanticVersion runningVersion, bool newerAvailable) : IReleaseFeed
{
    public Task<ReleaseInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = newerAvailable
            ? SemanticVersion.Parse("0.2.0")
            : runningVersion;
        var release = new ReleaseInfo(
            TagName: "v" + version,
            Version: version,
            Title: "BDO Combo Setup " + version,
            Notes: "Demo release notes.\n\n- Updates: check, banner and one-click install.\n- Portable build: opens the release page.",
            PageUrl: UpdateRepository.ReleasesPage,
            PublishedAt: DateTimeOffset.UnixEpoch,
            Assets: []);
        return Task.FromResult(release);
    }
}
