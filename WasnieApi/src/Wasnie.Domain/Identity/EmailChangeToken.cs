using Wasnie.Domain.Common;

namespace Wasnie.Domain.Identity;

public sealed class EmailChangeToken : Entity
{
    public string UserId { get; private set; } = string.Empty;
    public string NewEmail { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsValid(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    private EmailChangeToken() { }

    public static EmailChangeToken Create(
        Guid id,
        string userId,
        string newEmail,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserId = userId,
            NewEmail = newEmail,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
