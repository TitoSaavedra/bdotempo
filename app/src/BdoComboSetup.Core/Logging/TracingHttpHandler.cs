namespace BdoComboSetup.Core.Logging;

/// <summary>
/// Outgoing HTTP handler of the app. Each call runs as an operation of the logger (<c>http.request.start</c> and
/// <c>http.request.end</c>), and the request carries the W3C <c>traceparent</c> of that operation, plus <c>tracestate</c>
/// when the context has one. The header parent-id is therefore the span-id logged for the call.
/// Only the method, the host and the HTTP status are logged: no URL path, no query, no headers and no bodies.
/// The duration covers the wait for the response headers; the body is read by the caller afterwards.
/// </summary>
public sealed class TracingHttpHandler(AppLogger logger) : DelegatingHandler
{
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = _logger.BeginOperation("http.request", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["method"] = request.Method.Method,
            ["host"] = request.RequestUri?.Host ?? "unknown",
        });

        // Read after BeginOperation: the call's own span is the current one, so the header and the log line agree.
        ApplyTraceHeaders(request, _logger.CurrentTraceContext);

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(true);
            scope.SetField("http_status", (int)response.StatusCode);
            return response;
        }
        catch (Exception ex)
        {
            scope.SetField("error_type", ex.GetType().Name);
            scope.Fail(ex);
            throw;
        }
    }

    private static void ApplyTraceHeaders(HttpRequestMessage request, W3cTraceContext trace)
    {
        request.Headers.Remove(W3cTraceContext.TraceparentHeader);
        request.Headers.TryAddWithoutValidation(W3cTraceContext.TraceparentHeader, trace.Traceparent);

        request.Headers.Remove(W3cTraceContext.TracestateHeader);
        if (!string.IsNullOrEmpty(trace.TraceState))
        {
            request.Headers.TryAddWithoutValidation(W3cTraceContext.TracestateHeader, trace.TraceState);
        }
    }
}
