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

    public bool IsValid => !IsRevoked && DateTimeOffset.UtcNow < ExpiresAt;

    private RefreshToken() { }

    public static RefreshToken Create(string token, string userId, Guid tenantId, DateTimeOffset expiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Token = token,
            UserId = userId,
            TenantId = tenantId,
            ExpiresAt = expiresAt,
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

    public void Revoke()
    {
        IsRevoked = true;
        RevokedAt = DateTimeOffset.UtcNow;
    }
}
