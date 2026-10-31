using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BdoComboSetup.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Structured JSON-lines logger written to %LOCALAPPDATA%\BDOTempo\logs.
/// Every line carries a W3C trace-id and span-id. They come from the current
/// <see cref="Activity"/> when there is one, otherwise from the session span.
/// Operation spans reuse the session trace-id, so one run shares one trace.
/// </summary>
public sealed class AppLogger : IDisposable
{
    public const string SourceName = "BDOTempo";

    private static readonly ActivitySource Source = new(SourceName, "0.1.0");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Keeps "+" in timestamps and non-ASCII text readable; JSON stays valid either way.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly ActivityListener _listener;
    private readonly Activity _session;

    public AppLogger(string logDirectory, string sessionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        Directory.CreateDirectory(logDirectory);
        _filePath = Path.Combine(logDirectory, $"app-{DateTime.UtcNow:yyyyMMdd}.log");

        // Without a listener ActivitySource.StartActivity returns null and there are no W3C ids.
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);

        _session = Source.StartActivity(sessionName, ActivityKind.Internal)
            ?? throw new InvalidOperationException("Activity listener is not active; cannot create the log session span.");
    }

    /// <summary>The default log directory: %LOCALAPPDATA%\BDOTempo\logs.</summary>
    public static string DefaultLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BDOTempo", "logs");

    public static AppLogger CreateDefault(string sessionName) => new(DefaultLogDirectory, sessionName);

    /// <summary>The W3C trace-id of this run, in lowercase hex (32 chars).</summary>
    public string SessionTraceId => _session.TraceId.ToHexString();

    public string FilePath => _filePath;

    public void Debug(string eventName, string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Log(LogLevel.Debug, eventName, message, fields);

    public void Info(string eventName, string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Log(LogLevel.Info, eventName, message, fields);

    public void Warn(string eventName, string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Log(LogLevel.Warn, eventName, message, fields);

    public void Error(string eventName, string message, Exception exception, IReadOnlyDictionary<string, object?>? fields = null) =>
        Log(LogLevel.Error, eventName, message, fields, exception);

    /// <summary>
    /// Starts a timed operation. It logs "&lt;name&gt;.start" now and "&lt;name&gt;.end" on dispose,
    /// with duration_ms and status. Call <see cref="OperationScope.Fail"/> before disposing on errors.
    /// </summary>
    public OperationScope BeginOperation(string name, IReadOnlyDictionary<string, object?>? fields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var activity = Source.StartActivity(name, ActivityKind.Internal, _session.Context);
        var scope = new OperationScope(this, name, activity);
        Log(LogLevel.Info, name + ".start", "Operation started", fields);
        return scope;
    }

    public void Log(
        LogLevel level,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? fields = null,
        Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var current = Activity.Current ?? _session;
        var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ts"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["level"] = level.ToString().ToLowerInvariant(),
            ["event"] = eventName,
            ["message"] = message,
            ["trace_id"] = current.TraceId.ToHexString(),
            ["span_id"] = current.SpanId.ToHexString(),
        };

        if (fields is not null)
        {
            foreach (var (key, value) in fields)
            {
                entry.TryAdd(key, value);
            }
        }

        if (exception is not null)
        {
            entry["error"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = exception.GetType().FullName,
                ["message"] = exception.Message,
                ["stack"] = exception.ToString(),
            };
        }

        Write(JsonSerializer.Serialize(entry, JsonOptions));
    }

    private void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log file itself is unreachable, so the only remaining channel is the debugger output.
            System.Diagnostics.Debug.WriteLine($"BdoComboSetup logger could not write to its log file: {ex}");
        }
    }

    public void Dispose()
    {
        _session.Stop();
        _session.Dispose();
        _listener.Dispose();
    }
}
