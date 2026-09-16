using System.Diagnostics;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Where one assistant turn spends its time, split into the PROVIDER's time and OURS (KAN-74).
///
/// ★★ WHY THE SPLIT IS THE WHOLE POINT. A slow "hola" can be slow for two unrelated reasons: the model calls themselves
/// (OpenRouter's routing, the model's load, a reasoning model thinking before its first visible token) or our pipeline
/// around them (database round trips, serial calls that could be avoided). Optimising the wrong one is wasted work, so
/// every turn logs both — measured, not estimated.
///
/// ★ "PROVIDER" IS THE WALL TIME OF EACH MODEL CALL as this process sees it: request out to response in (for the
/// streamed answer, to the last fragment). It includes the network, which is not ours to fix either. "Ours" is
/// everything else: <c>total − provider</c>.
///
/// ★ NUMBERS ONLY. No question, no answer, no tool payload reaches the log — the same privacy line the assistant's other
/// logs keep.
/// </summary>
public sealed class AssistantTurnTimer
{
    private readonly Func<long> _timestamp;
    private readonly long _start;

    private long? _routerStart, _routerEnd;
    private long? _dispatcherStart, _dispatcherEnd;
    private long? _toolStart, _toolEnd;
    private long? _answerStart, _answerFirstFragment, _answerEnd;

    /// <param name="timestamp">The clock, as <see cref="Stopwatch.GetTimestamp"/> ticks. Injected only by tests.</param>
    public AssistantTurnTimer(Func<long>? timestamp = null)
    {
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _start = _timestamp();
    }

    public void RouterStarted() => _routerStart = _timestamp();
    public void RouterEnded() => _routerEnd = _timestamp();
    public void DispatcherStarted() => _dispatcherStart = _timestamp();
    public void DispatcherEnded() => _dispatcherEnd = _timestamp();
    public void ToolStarted() => _toolStart = _timestamp();
    public void ToolEnded() => _toolEnd = _timestamp();
    public void AnswerStarted() => _answerStart = _timestamp();

    /// <summary>Only the FIRST call counts: it is the moment the user starts reading.</summary>
    public void AnswerFragment() => _answerFirstFragment ??= _timestamp();

    public void AnswerEnded() => _answerEnd = _timestamp();

    /// <summary>The breakdown as of now. Steps that never ran are null, never zero (§B3: "not run" ≠ "took no time").</summary>
    public AssistantTurnTiming Snapshot()
    {
        var now = _timestamp();

        var router = Span(_routerStart, _routerEnd, now);
        var dispatcher = Span(_dispatcherStart, _dispatcherEnd, now);
        var answer = Span(_answerStart, _answerEnd, now);
        var ttft = _answerStart is { } a && _answerFirstFragment is { } f ? Ms(a, f) : (long?)null;

        var total = Ms(_start, now);

        // ★★ THE CLASSIFIERS ARE MEASURED AS ONE WALL-CLOCK SPAN, NOT AS A SUM. Router and dispatcher run in PARALLEL
        // (KAN-74), so adding their durations would count the same seconds twice and push "ours" below zero. The time the
        // turn actually waited on them is from the first of the two starting to the last of the two finishing.
        var classifiers = ClassifierWallMs(now);
        var provider = (classifiers ?? 0) + (answer ?? 0);

        // Everything before the first model call: middleware-free handler work (entitlement, loading the thread, storing
        // the question). Measured to whichever model call started first.
        var firstModelCall = _routerStart ?? _dispatcherStart ?? _answerStart;
        var beforeModel = firstModelCall is { } m ? Ms(_start, m) : (long?)null;

        return new AssistantTurnTiming(
            TotalMs: total,
            ProviderMs: provider,
            OursMs: total - provider,
            ClassifiersMs: classifiers,
            BeforeFirstModelCallMs: beforeModel,
            RouterMs: router,
            DispatcherMs: dispatcher,
            ToolMs: Span(_toolStart, _toolEnd, now),
            AnswerTimeToFirstTokenMs: ttft,
            AnswerMs: answer);
    }

    private long? ClassifierWallMs(long now)
    {
        long[] starts = new[] { _routerStart, _dispatcherStart }.Where(t => t is not null).Select(t => t!.Value).ToArray();
        if (starts.Length == 0)
        {
            return null;
        }

        var ends = new[] { (_routerStart, _routerEnd), (_dispatcherStart, _dispatcherEnd) }
            .Where(p => p.Item1 is not null)
            .Select(p => p.Item2 ?? now);

        return Ms(starts.Min(), ends.Max());
    }

    /// <summary>A step that started but never ended (failed, cancelled) is measured up to now.</summary>
    private long? Span(long? start, long? end, long now) => start is { } s ? Ms(s, end ?? now) : null;

    private static long Ms(long from, long to) =>
        (long)Math.Round((to - from) * 1000.0 / Stopwatch.Frequency);
}

/// <summary>One turn's timing breakdown, in milliseconds. See <see cref="AssistantTurnTimer"/>.</summary>
public sealed record AssistantTurnTiming(
    long TotalMs,
    long ProviderMs,
    long OursMs,
    long? BeforeFirstModelCallMs,
    long? ClassifiersMs,
    long? RouterMs,
    long? DispatcherMs,
    long? ToolMs,
    long? AnswerTimeToFirstTokenMs,
    long? AnswerMs);
