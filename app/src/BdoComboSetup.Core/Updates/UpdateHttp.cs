namespace BdoComboSetup.Core.Updates;

/// <summary>
/// The only way the updater talks to the network. The app passes a <see cref="HttpClient"/> wrapper; the tests pass a fake
/// that answers from a script. The caller disposes the response.
/// </summary>
public interface IUpdateHttpClient
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>The production transport: one <see cref="HttpClient"/> for the process, reads headers first (downloads stream).</summary>
public sealed class HttpClientUpdateTransport(HttpClient client) : IUpdateHttpClient
{
    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    /// <summary>
    /// The client the app uses. No credentials, no cookies: the requests are anonymous GET calls to public endpoints.
    /// The timeout is infinite on purpose (the download is large); the lookup sets its own timeout per call.
    /// </summary>
    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
