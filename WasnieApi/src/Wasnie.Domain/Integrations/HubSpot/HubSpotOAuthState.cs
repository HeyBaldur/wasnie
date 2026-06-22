using Wasnie.Domain.Common;

namespace Wasnie.Domain.Integrations.HubSpot;

/// <summary>
/// Short-lived anti-CSRF <c>state</c> for the HubSpot OAuth handshake.
///
/// Created (server-side) when an authenticated user starts a connection, and consumed once at the
/// OAuth callback. The callback is an anonymous top-level browser redirect from HubSpot (no JWT), so
/// it CANNOT read the tenant from the request — it recovers the tenant/user from this row. That is why
/// this table has NO tenant query filter: the callback looks it up by the random <see cref="Entity.Id"/>
/// (the state value) and trusts the stored <see cref="TenantId"/>.
/// </summary>
public sealed class HubSpotOAuthState : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>The user who initiated the connection (for ConnectedBy + audit).</summary>
    public string InitiatedByUserId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private HubSpotOAuthState() { }

    public static HubSpotOAuthState Create(
        Guid id,
        Guid tenantId,
        string initiatedByUserId,
        DateTimeOffset now,
        TimeSpan ttl) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            InitiatedByUserId = initiatedByUserId,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl),
        };

    public bool IsValid(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
