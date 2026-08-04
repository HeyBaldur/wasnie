using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Assistant;

public enum AssistantMessageRole
{
    User = 0,
    Assistant = 1,
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
        string? payload = null)
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

        return new AssistantMessage
        {
            Id = id,
            ConversationId = conversationId,
            TenantId = tenantId,
            Role = role,
            Content = trimmed,
            Payload = payload,
            Sequence = sequence,
            CreatedAt = now,
        };
    }
}
