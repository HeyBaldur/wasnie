using Wasnie.Domain.Common;

namespace Wasnie.Domain.Integrations.HubSpot;

/// <summary>
/// A tenant's connection to a HubSpot account (one per tenant).
///
/// SECURITY: AccessToken and RefreshToken are HubSpot CRM credentials. They are stored ENCRYPTED at rest
/// (the *Encrypted properties hold ciphertext only — the domain never sees plaintext), are NEVER logged,
/// and are NEVER returned to the frontend.
/// </summary>
public sealed class HubSpotConnection : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>HubSpot portal/hub id (identifies the connected HubSpot account). Not secret.</summary>
    public long PortalId { get; private set; }

    /// <summary>Ciphertext of the access token. Never plaintext, never logged, never sent to the client.</summary>
    public string AccessTokenEncrypted { get; private set; } = string.Empty;

    /// <summary>Ciphertext of the refresh token. Never plaintext, never logged, never sent to the client.</summary>
    public string RefreshTokenEncrypted { get; private set; } = string.Empty;

    /// <summary>When the current access token expires (computed from HubSpot's <c>expires_in</c>).</summary>
    public DateTimeOffset TokenExpiresAt { get; private set; }

    public HubSpotConnectionStatus Status { get; private set; } = HubSpotConnectionStatus.Connected;

    /// <summary>
    /// Human-readable explanation of why the connection is in its current state (esp. NeedsReconnect /
    /// Disconnected), shown to the user in the UI. Null when freshly Connected.
    /// </summary>
    public string? StatusReason { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }
    public string ConnectedBy { get; private set; } = string.Empty;

    public DateTimeOffset? DisconnectedAt { get; private set; }
    public string? DisconnectedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic-concurrency token (SQL Server rowversion).</summary>
    public byte[] RowVersion { get; private set; } = [];

    private HubSpotConnection() { }

    public static HubSpotConnection Create(
        Guid id,
        Guid tenantId,
        long portalId,
        string accessTokenEncrypted,
        string refreshTokenEncrypted,
        DateTimeOffset tokenExpiresAt,
        string connectedBy,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            PortalId = portalId,
            AccessTokenEncrypted = accessTokenEncrypted,
            RefreshTokenEncrypted = refreshTokenEncrypted,
            TokenExpiresAt = tokenExpiresAt,
            Status = HubSpotConnectionStatus.Connected,
            StatusReason = null,
            ConnectedAt = now,
            ConnectedBy = connectedBy,
            DisconnectedAt = null,
            DisconnectedBy = null,
            UpdatedAt = now,
        };

    /// <summary>
    /// Re-establish a connection on an EXISTING row (e.g. reconnecting after NeedsReconnect, or
    /// re-authorising a previously Disconnected tenant). Replaces tokens and returns to Connected.
    /// </summary>
    public void Reconnect(
        long portalId,
        string accessTokenEncrypted,
        string refreshTokenEncrypted,
        DateTimeOffset tokenExpiresAt,
        string connectedBy,
        DateTimeOffset now)
    {
        PortalId = portalId;
        AccessTokenEncrypted = accessTokenEncrypted;
        RefreshTokenEncrypted = refreshTokenEncrypted;
        TokenExpiresAt = tokenExpiresAt;
        Status = HubSpotConnectionStatus.Connected;
        StatusReason = null;
        ConnectedAt = now;
        ConnectedBy = connectedBy;
        DisconnectedAt = null;
        DisconnectedBy = null;
        UpdatedAt = now;
    }

    /// <summary>Persist freshly-refreshed tokens. The old access token is revoked by HubSpot on refresh.</summary>
    public void ApplyRefreshedTokens(
        string accessTokenEncrypted,
        string refreshTokenEncrypted,
        DateTimeOffset tokenExpiresAt,
        DateTimeOffset now)
    {
        AccessTokenEncrypted = accessTokenEncrypted;
        RefreshTokenEncrypted = refreshTokenEncrypted;
        TokenExpiresAt = tokenExpiresAt;
        Status = HubSpotConnectionStatus.Connected;
        StatusReason = null;
        UpdatedAt = now;
    }

    /// <summary>Mark the connection as broken (refresh failed irrecoverably). Surfaced in the UI via StatusReason.</summary>
    public void MarkNeedsReconnect(string reason, DateTimeOffset now)
    {
        Status = HubSpotConnectionStatus.NeedsReconnect;
        StatusReason = reason;
        UpdatedAt = now;
    }

    /// <summary>User-initiated disconnect. Clears the stored CRM credentials but keeps the row for audit.</summary>
    public void Disconnect(string reason, string disconnectedBy, DateTimeOffset now)
    {
        Status = HubSpotConnectionStatus.Disconnected;
        StatusReason = reason;
        AccessTokenEncrypted = string.Empty;
        RefreshTokenEncrypted = string.Empty;
        DisconnectedAt = now;
        DisconnectedBy = disconnectedBy;
        UpdatedAt = now;
    }
}
