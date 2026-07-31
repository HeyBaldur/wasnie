using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Assistant;

/// <summary>
/// One chat thread between a single user and the assistant.
///
/// ★ OWNERSHIP IS PART OF THE IDENTITY, not a filter someone remembers to apply. A conversation
/// belongs to exactly one (TenantId, UserId) pair and there is no operation that moves it, shares it
/// or widens it. Every read path must match BOTH — the tenant query filter alone is not enough,
/// because two users of the same tenant must not read each other's chats either.
///
/// The assistant is a private notebook, not a team channel. If a shared mode is ever wanted, it is a
/// new concept with its own rules, not a relaxed filter on this one.
/// </summary>
public sealed class AssistantConversation : Entity
{
    public const int MaxTitleLength = 200;

    public Guid TenantId { get; private set; }

    /// <summary>
    /// The ASP.NET Identity user id of the owner. 450 chars to match the Identity key width used
    /// elsewhere in the schema (see RefreshToken / audit actor columns).
    /// </summary>
    public string UserId { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Bumped whenever a message is appended, so "most recent conversation" is a single indexed sort
    /// instead of a join against the messages table.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    private AssistantConversation() { }

    public static AssistantConversation Start(
        Guid id,
        Guid tenantId,
        string userId,
        string title,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");

        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("UserId must not be empty.");

        return new AssistantConversation
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            Title = NormalizeTitle(title),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Rename(string title, DateTimeOffset now)
    {
        Title = NormalizeTitle(title);
        UpdatedAt = now;
    }

    /// <summary>Called when a message is appended, so the list can sort by real activity.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>
    /// The ONE place that answers "may this principal see this conversation?". Both halves are
    /// required: same tenant AND same user. Callers filter in SQL, but a mistake there should still
    /// fail here rather than return someone else's chat.
    /// </summary>
    public bool IsOwnedBy(Guid tenantId, string? userId) =>
        TenantId == tenantId
        && !string.IsNullOrEmpty(userId)
        && string.Equals(UserId, userId, StringComparison.Ordinal);

    private static string NormalizeTitle(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new DomainException("Title must not be empty.");

        return trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed;
    }
}
