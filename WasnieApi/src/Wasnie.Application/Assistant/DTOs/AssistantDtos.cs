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
/// <param name="Status">
/// How the turn ended: `Complete`, or `Cancelled` when the user stopped the answer mid-write.
///
/// ★ THIS IS WHAT MAKES A CANCELLATION SURVIVE THE RELOAD. The partial text is stored, so without the
/// status beside it a reopened conversation would show an answer that stops mid-sentence as if the
/// assistant had chosen to end there. The client renders the notice from THIS, never from anything it
/// remembers about the session.
/// </param>
public sealed record AssistantMessageDto(
    Guid Id,
    string Role,
    string Content,
    string? Payload,
    int Sequence,
    DateTimeOffset CreatedAt,
    string Status);

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
/// Whether the current user may use the assistant, and — when they may not — whether that is
/// something they can fix by paying.
///
/// The second flag is NOT the client learning "why" for its own sake: it is the difference between
/// two different UIs. <c>Enabled=false, RequiresUpgrade=false</c> means the person has no seat and the
/// entry point is HIDDEN (a permission the client must not advertise). <c>RequiresUpgrade=true</c>
/// means the workspace is on Free and the entry point is LOCKED with a link to the plans, because an
/// upgrade is a thing this user can actually go and do. Every other reason stays behind the boolean.
/// </summary>
public sealed record AssistantEntitlementDto(bool Enabled, bool RequiresUpgrade);
