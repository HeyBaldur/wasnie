namespace Wasnie.Application.Assistant.DTOs;

/// <summary>One conversation as it appears in the history list. No messages — the list does not need them.</summary>
public sealed record AssistantConversationSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount);

/// <summary>
/// One turn.
/// </summary>
/// <param name="Payload">
/// Always null in this piece. Exposed from day one so the client contract does not change when later
/// pieces start attaching structure to a turn — a field that appears later is a breaking change for
/// every consumer; a field that is always null is not.
/// </param>
public sealed record AssistantMessageDto(
    Guid Id,
    string Role,
    string Content,
    string? Payload,
    int Sequence,
    DateTimeOffset CreatedAt);

/// <summary>A conversation with its turns, in order.</summary>
/// <param name="LastTurnUnanswered">
/// True when the thread ends on a question that never got its reply — the assistant failed, and the
/// client should show the warning and offer a retry.
///
/// ★ THIS IS WHY THE FAILURE SURVIVES A REFRESH. It is computed from the stored turns on every read
/// (see <see cref="Common.UnansweredTurn"/>), not held in the browser, so reloading the page cannot
/// lose it — and not held as a flag either, so it cannot contradict the messages beside it.
///
/// Sent from the server rather than derived in the client so there is ONE definition of "this failed".
/// Two would eventually disagree, and the client's would be the one the user sees.
/// </param>
public sealed record AssistantConversationDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AssistantMessageDto> Messages,
    bool LastTurnUnanswered);

/// <summary>
/// What one exchange produced: the user's turn and the assistant's reply, both already persisted.
/// Returning the pair (rather than just the reply) means the client never has to guess what the server
/// stored for its own message — no optimistic copy that can drift from the row.
/// </summary>
public sealed record AssistantExchangeDto(
    AssistantMessageDto UserMessage,
    AssistantMessageDto AssistantMessage);

/// <summary>
/// Whether the current user may use the assistant. A single boolean on purpose: the client hides or
/// shows the entry point and has no business knowing WHY — role today, paid seat tomorrow.
/// </summary>
public sealed record AssistantEntitlementDto(bool Enabled);
