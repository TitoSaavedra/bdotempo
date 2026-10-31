using System.Text.Json;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Tests;

public sealed class AppLoggerTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private IEnumerable<JsonElement> ReadLines()
    {
        foreach (var line in File.ReadAllLines(_log.Logger.FilePath))
        {
            yield return JsonDocument.Parse(line).RootElement.Clone();
        }
    }

    [Fact]
    public void Every_line_is_json_with_level_timestamp_and_w3c_ids()
    {
        _log.Logger.Info("test.event", "hello", new Dictionary<string, object?> { ["k"] = 1 });

        var line = ReadLines().Single(e => e.GetProperty("event").GetString() == "test.event");
        Assert.Equal("info", line.GetProperty("level").GetString());
        Assert.Equal("hello", line.GetProperty("message").GetString());
        Assert.Equal(1, line.GetProperty("k").GetInt32());
        Assert.False(string.IsNullOrEmpty(line.GetProperty("ts").GetString()));

        var traceId = line.GetProperty("trace_id").GetString()!;
        var spanId = line.GetProperty("span_id").GetString()!;
        Assert.Matches("^[0-9a-f]{32}$", traceId);
        Assert.Matches("^[0-9a-f]{16}$", spanId);
    }

    [Fact]
    public void Operation_logs_start_and_end_with_duration_and_share_the_session_trace()
    {
        using (var op = _log.Logger.BeginOperation("test.op"))
        {
            _log.Logger.Debug("test.inside", "inside the operation");
        }

        var lines = ReadLines().ToList();
        var start = lines.Single(e => e.GetProperty("event").GetString() == "test.op.start");
        var end = lines.Single(e => e.GetProperty("event").GetString() == "test.op.end");
        var inside = lines.Single(e => e.GetProperty("event").GetString() == "test.inside");

        Assert.Equal(_log.Logger.SessionTraceId, end.GetProperty("trace_id").GetString());
        Assert.Equal(start.GetProperty("span_id").GetString(), inside.GetProperty("span_id").GetString());
        Assert.Equal("ok", end.GetProperty("status").GetString());
        Assert.True(end.GetProperty("duration_ms").GetDouble() >= 0);
    }

    [Fact]
    public void Failed_operation_logs_the_error_with_its_type_and_stack()
    {
        using (var op = _log.Logger.BeginOperation("test.fail"))
        {
            op.Fail(new InvalidOperationException("broken"));
        }

        var end = ReadLines().Single(e => e.GetProperty("event").GetString() == "test.fail.end");
        Assert.Equal("error", end.GetProperty("status").GetString());
        Assert.Equal("error", end.GetProperty("level").GetString());
        var error = end.GetProperty("error");
        Assert.Equal(typeof(InvalidOperationException).FullName, error.GetProperty("type").GetString());
        Assert.Equal("broken", error.GetProperty("message").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("stack").GetString()));
    }

}
