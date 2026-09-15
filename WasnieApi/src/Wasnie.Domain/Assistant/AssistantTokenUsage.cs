using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Assistant;

/// <summary>
/// One model call's token usage, attributed to an account (KAN-80).
///
/// ★ APPEND-ONLY, ONE ROW PER CALL — NOT A COUNTER ON THE TENANT. A running total cannot be reconciled with OpenRouter:
/// when the two disagree there is nothing to compare line by line. Rows carry the call, the model, the upstream vendor
/// and the moment, which is exactly what the provider's own activity log lists. The totals the screens show are SUMS of
/// these rows, derived every time (§B5) — there is no stored total to drift.
///
/// ★ NOT A MONEY TABLE. <see cref="Cost"/> is the aggregator's own credit figure, kept for reconciliation; euros per
/// tenant are KAN-23's.
///
/// ★ NULL TOKENS ARE A FACT, NOT A GAP TO FILL. A stream cancelled before its final usage message leaves the row with
/// null tokens: the call happened, the provider did not say how much it spent. Writing zero would claim it spent nothing.
/// </summary>
public sealed class AssistantTokenUsage : Entity
{
    public const int MaxModelLength = 128;
    public const int MaxUpstreamLength = 64;

    public Guid TenantId { get; private set; }

    /// <summary>ASP.NET Identity user id; empty is refused — every model call is made on someone's behalf.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The conversation the call served. Null only for a call made outside one.</summary>
    public Guid? ConversationId { get; private set; }

    /// <summary><c>Router</c>, <c>Dispatcher</c> or <c>Answer</c>, stored by name.</summary>
    public string Call { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    /// <summary>The vendor that actually served the call (OpenRouter's <c>provider</c>). Null when not reported.</summary>
    public string? Upstream { get; private set; }

    public int? PromptTokens { get; private set; }

    /// <summary>Output tokens, reasoning included.</summary>
    public int? CompletionTokens { get; private set; }

    public int? ReasoningTokens { get; private set; }

    /// <summary>The aggregator's credits for this call. Not euros.</summary>
    public decimal? Cost { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private AssistantTokenUsage() { }

    public static AssistantTokenUsage Create(
        Guid id, Guid tenantId, string userId, Guid? conversationId, string call, string model, string? upstream,
        int? promptTokens, int? completionTokens, int? reasoningTokens, decimal? cost, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("UserId must not be empty.");
        if (string.IsNullOrWhiteSpace(call))
            throw new DomainException("Call must not be empty.");
        if (promptTokens < 0 || completionTokens < 0 || reasoningTokens < 0)
            throw new DomainException("Token counts cannot be negative.");

        return new AssistantTokenUsage
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            ConversationId = conversationId,
            Call = call,
            Model = Truncate(model, MaxModelLength),
            Upstream = upstream is null ? null : Truncate(upstream, MaxUpstreamLength),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            ReasoningTokens = reasoningTokens,
            Cost = cost,
            CreatedAt = now,
        };
    }

    // A vendor that returns a longer name must not make the usage row fail to save — losing the count is worse.
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
