using System.Diagnostics;

namespace BdoComboSetup.Core.Logging;

/// <summary>
/// A W3C Trace Context: the ids that the <c>traceparent</c> and <c>tracestate</c> headers carry between processes and services.
/// The ids are lowercase hex, as the standard requires.
/// </summary>
public sealed record W3cTraceContext(string TraceId, string SpanId, byte Flags, string? TraceState)
{
    public const string TraceparentHeader = "traceparent";
    public const string TracestateHeader = "tracestate";

    /// <summary>The "sampled" bit of the trace flags (the trace is recorded).</summary>
    public const byte SampledFlag = 0x01;

    /// <summary>The traceparent value: version 00, trace-id, parent-id (the span-id) and flags.</summary>
    public string Traceparent => $"00-{TraceId}-{SpanId}-{Flags:x2}";

    /// <summary>A new, sampled trace with random ids. Used when no operation or session context is available.</summary>
    public static W3cTraceContext NewRoot() => new(
        ActivityTraceId.CreateRandom().ToHexString(),
        ActivitySpanId.CreateRandom().ToHexString(),
        SampledFlag,
        null);
}
