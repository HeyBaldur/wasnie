using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Assistant;

public enum AssistantMessageRole
{
    User = 0,
    Assistant = 1,
}

/// <summary>
/// How a stored turn ENDED. Terminal at the moment it is written and never changed afterwards.
///
/// ★ WHY THIS EXISTS WHEN "FAILED" DELIBERATELY DOES NOT. <see cref="AssistantMessageRole.User"/> turns
/// that never got a reply are the record of a failure, and <c>UnansweredTurn</c> argues at length
/// against storing a flag for it: the absence of the answer already IS the fact, and a second copy of a
/// fact drifts from the first. That argument holds — and it does not reach this.
///
/// A cancelled turn is the one outcome the absence cannot express, because there is something present:
/// the words the model had already written and the user had already read. Deleting them to keep the
/// "nothing partial is ever stored" rule would erase what was on their screen; storing them unmarked
/// would put a sentence that stops mid-word into the thread wearing the clothes of a finished answer,
/// and the assistant would appear to have said it. So the row is stored WITH the reason it stops.
///
/// ★ IT CANNOT DRIFT, because nothing ever writes it twice. The status is decided by the one path that
/// creates the row and there is no operation anywhere that changes it — no "mark complete", no clear.
/// The flag <c>UnansweredTurn</c> refused was a mutable one on a turn whose truth could change
/// underneath it; this is part of what the row IS.
/// </summary>
public enum AssistantMessageStatus
{
    /// <summary>The turn finished on its own. Every user turn and every completed answer.</summary>
    Complete = 0,

    /// <summary>
    /// The user stopped the answer while it was being written. The content is whatever had arrived —
    /// real words, deliberately kept, and marked so the client can say where they stop.
    /// </summary>
    Cancelled = 1,
}

/// <summary>
/// One turn in a conversation.
///
/// ★ WHY <see cref="Payload"/> EXISTS BEFORE ANYTHING FILLS IT. A message that is only a string is
/// enough for a chat and nothing else. The pieces already planned on top of this one — retrieved
/// document references (RAG), the screen context a question was asked from, and the typed JSON that
/// pre-fills a Plan+Quota form — all attach STRUCTURE to a turn. Adding a nullable column now costs a
/// column; adding it after there is chat history costs a migration over live conversations plus a
/// backfill decision for every existing row. So the column ships empty, on purpose.
///
/// It is deliberately untyped JSON at this layer: what goes in it is not decided yet, and inventing a
/// schema today would be inventing the answer to a question nobody has asked. Nothing writes it in
/// this piece — <see cref="Create"/> takes it, and every current caller passes null.
/// </summary>
public sealed class AssistantMessage : Entity
{
    public const int MaxContentLength = 8000;

    /// <summary>
    /// The stored content of the assistant's stand-in reply while no model is connected.
    ///
    /// A SENTINEL, not a sentence: the row must not carry English text, because the same history is
    /// read by a Spanish and a Polish user and the UI translates this marker at render time. When a
    /// real model answers, its actual words are stored and this constant stops appearing in new rows —
    /// old rows keep rendering as the translated placeholder, which is exactly what they were.
    /// </summary>
    public const string NotConnectedPlaceholder = "__ASSISTANT_NOT_CONNECTED__";

    public Guid ConversationId { get; private set; }

    /// <summary>
    /// Denormalized from the conversation so every read path can filter without a join — and so a
    /// query that forgets the join cannot accidentally return another tenant's messages.
    /// </summary>
    public Guid TenantId { get; private set; }

    public AssistantMessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    /// <summary>
    /// How this turn ended — see <see cref="AssistantMessageStatus"/>. Written once, with the row, and
    /// never modified: there is deliberately no method here that changes it.
    /// </summary>
    public AssistantMessageStatus Status { get; private set; } = AssistantMessageStatus.Complete;

    /// <summary>
    /// Reserved for structure that later pieces attach to a turn (RAG references, screen context,
    /// pre-fill JSON). ALWAYS null today. See the type-level note above for why it exists already.
    /// </summary>
    public string? Payload { get; private set; }

    /// <summary>
    /// Position within the conversation, assigned by the writer. Ordering by timestamp would be a
    /// coin flip for the user turn and its reply, which are written in the same instant.
    /// </summary>
    public int Sequence { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private AssistantMessage() { }

    public static AssistantMessage Create(
        Guid id,
        Guid conversationId,
        Guid tenantId,
        AssistantMessageRole role,
        string content,
        int sequence,
        DateTimeOffset now,
        string? payload = null,
        AssistantMessageStatus status = AssistantMessageStatus.Complete)
    {
        if (conversationId == Guid.Empty)
            throw new DomainException("ConversationId must not be empty.");

        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");

        if (sequence < 0)
            throw new DomainException("Sequence must not be negative.");

        var trimmed = (content ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new DomainException("Message content must not be empty.");

        if (trimmed.Length > MaxContentLength)
            throw new DomainException($"Message content must not exceed {MaxContentLength} characters.");

        // ★ ONLY AN ANSWER CAN BE CANCELLED. A question is typed and sent in one motion — there is no
        // interval during which the user could stop it — so a cancelled user turn would be a state the
        // product cannot reach, and the client would render "response cancelled" under words the user
        // themselves wrote.
        if (status == AssistantMessageStatus.Cancelled && role != AssistantMessageRole.Assistant)
            throw new DomainException("Only an assistant turn can be cancelled.");

        return new AssistantMessage
        {
            Id = id,
            ConversationId = conversationId,
            TenantId = tenantId,
            Role = role,
            Content = trimmed,
            Status = status,
            Payload = payload,
            Sequence = sequence,
            CreatedAt = now,
        };
    }
}
