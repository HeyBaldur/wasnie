using Wasnie.Domain.Common;

namespace Wasnie.Domain.Identity;

public sealed class RefreshToken : Entity
{
    public string Token { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsValidAt(DateTimeOffset now) => !IsRevoked && now < ExpiresAt;

    private RefreshToken() { }

    public static RefreshToken Create(string token, string userId, Guid tenantId, DateTimeOffset expiresAt, Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            Token = token,
            UserId = userId,
            TenantId = tenantId,
            ExpiresAt = expiresAt,
            IsRevoked = false,
            CreatedAt = now
        };

    public void Revoke(DateTimeOffset now)
    {
        IsRevoked = true;
        RevokedAt = now;
    }
}
