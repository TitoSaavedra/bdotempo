using System.Diagnostics;

namespace BdoComboSetup.Core.Logging;

/// <summary>
/// A timed operation created by <see cref="AppLogger.BeginOperation"/>.
/// Dispose logs the end event with duration_ms and status (ok or error).
/// </summary>
public sealed class OperationScope : IDisposable
{
    private readonly AppLogger _logger;
    private readonly string _name;
    private readonly Activity? _activity;
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private Exception? _failure;
    private bool _disposed;

    internal OperationScope(AppLogger logger, string name, Activity? activity)
    {
        _logger = logger;
        _name = name;
        _activity = activity;
    }

    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _failure = exception;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["duration_ms"] = Math.Round(Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds, 2),
            ["status"] = _failure is null ? "ok" : "error",
        };

        if (_failure is null)
        {
            _logger.Info(_name + ".end", "Operation finished", fields);
        }
        else
        {
            _logger.Log(LogLevel.Error, _name + ".end", "Operation failed", fields, _failure);
        }

        _activity?.Stop();
        _activity?.Dispose();
    }
}
