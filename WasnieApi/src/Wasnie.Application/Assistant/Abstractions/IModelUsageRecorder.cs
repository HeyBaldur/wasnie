namespace Wasnie.Application.Assistant.Abstractions;

/// <summary>Which of Zeke's model calls spent the tokens (KAN-80).</summary>
public enum ModelCall
{
    Router,
    Dispatcher,
    Answer,
}

/// <summary>
/// What one model call reported spending. Every token field is NULL when the provider did not report it (a stream that
/// was cancelled before its final usage message, a vendor that does not send usage) — never zero, because "not
/// reported" and "spent nothing" are different facts (§B3).
/// </summary>
/// <param name="PromptTokens">Input tokens.</param>
/// <param name="CompletionTokens">Output tokens, reasoning included (measured: reasoning ≤ completion on every call).</param>
/// <param name="ReasoningTokens">The reasoning share of <paramref name="CompletionTokens"/>; informational.</param>
/// <param name="Cost">What the aggregator says it charged, in its own credits. Null when not reported.</param>
public sealed record ModelUsage(
    ModelCall Call,
    string Model,
    string? Upstream,
    int? PromptTokens,
    int? CompletionTokens,
    int? ReasoningTokens,
    decimal? Cost);

/// <summary>
/// Collects the token usage of every model call in one request and persists it when the request ends (KAN-80).
///
/// ★★ PERSISTED AT THE END OF THE REQUEST, NOT BY EACH HANDLER. A turn has a dozen exits — errors, a stopped stream, a
/// refused allowance — and tokens spent on a turn that failed or was cancelled are still tokens the account consumed and
/// OpenRouter billed. A save placed on the happy path would undercount exactly the turns that went wrong.
///
/// ★ THREAD-SAFE BY CONTRACT. The router and the dispatcher run in PARALLEL (KAN-74), so two calls can record at once.
///
/// ★ THE PROVIDER RECORDS, THE HANDLER ONLY SAYS WHICH CONVERSATION. <see cref="IChatCompletionProvider"/> keeps returning
/// text; usage travels beside it instead of changing the interface every caller depends on.
/// </summary>
public interface IModelUsageRecorder
{
    /// <summary>Called by the provider once per call that got a successful response.</summary>
    void Record(ModelUsage usage);

    /// <summary>The conversation the calls of this request belong to. Optional: a call outside a conversation is still counted.</summary>
    void ForConversation(Guid conversationId);
}
