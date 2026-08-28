using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Assistant;

/// <summary>
/// One USER'S standing on one conversation: pinned or not, and since when.
///
/// ★★ WHY THIS IS A ROW AND NOT A COLUMN ON THE CONVERSATION. Pinning is a preference held BY A PERSON,
/// not a property of the thread. Today a conversation has exactly one owner, so a boolean on it would
/// behave identically and be less code — and it would be the wrong shape the day sharing arrives, which
/// it is already designed to: several people in a tenant looking at the same conversation, each wanting
/// their own pins. Moving it then means migrating live production data instead of adding a row.
///
/// The name is deliberately not <c>AssistantPin</c>. This is the table where "last read", "archived"
/// and "muted" belong when they are wanted — every one of them is the same shape, a fact about a
/// (user, conversation) pair. None of them is built here; the point is that adding one is a column, not
/// a table and a migration and a join.
///
/// ★ PinnedAt IS A NULLABLE INSTANT, NOT A BOOLEAN, and that buys two things a flag cannot. It orders
/// the pinned group for free — most recently pinned first, which is what people expect and what a
/// boolean would need a second column to express — and it answers "since when", which is a real
/// question about somebody's own data. Null means "not pinned", so unpinning keeps the row: the row is
/// this user's standing on the conversation, and it will hold more than one fact soon enough.
/// </summary>
public sealed class AssistantConversationState : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>
    /// The ASP.NET Identity user id this standing belongs to. 450 chars to match the Identity key width
    /// used elsewhere in the schema, exactly as <see cref="AssistantConversation.UserId"/> does.
    /// </summary>
    public string UserId { get; private set; } = string.Empty;

    public Guid ConversationId { get; private set; }

    /// <summary>When this user pinned it, or null when they have not. See the class comment.</summary>
    public DateTimeOffset? PinnedAt { get; private set; }

    /// <summary>
    /// When the row was created. Audit, not ordering — nothing reads it to sort. <c>PinnedAt</c> is what
    /// orders the pinned group, and it moves every time the conversation is pinned again while this
    /// stays put.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private AssistantConversationState() { }

    public static AssistantConversationState Create(
        Guid id, Guid tenantId, string userId, Guid conversationId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");

        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("UserId must not be empty.");

        if (conversationId == Guid.Empty)
            throw new DomainException("ConversationId must not be empty.");

        return new AssistantConversationState
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            ConversationId = conversationId,
            PinnedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>True when this user has this conversation pinned right now.</summary>
    public bool IsPinned => PinnedAt.HasValue;

    /// <summary>
    /// Pins it, or leaves an existing pin exactly where it is.
    ///
    /// ★ IDEMPOTENT, AND THE EARLY RETURN IS THE POINT RATHER THAN A MICRO-OPTIMISATION. Re-pinning
    /// something already pinned would otherwise rewrite <c>PinnedAt</c> — and since that is what orders
    /// the pinned group, a double-click would silently jump the conversation to the top of the pins.
    /// The user asked for it to be pinned; it is pinned; nothing about that request says "and move it".
    /// </summary>
    public void Pin(DateTimeOffset now)
    {
        if (PinnedAt.HasValue)
        {
            return;
        }

        PinnedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Unpins it. Idempotent for the same reason, and the row survives — see the class comment.</summary>
    public void Unpin(DateTimeOffset now)
    {
        if (!PinnedAt.HasValue)
        {
            return;
        }

        PinnedAt = null;
        UpdatedAt = now;
    }
}
