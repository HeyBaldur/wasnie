namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Supplies a usable (non-expired) HubSpot access token for a tenant, refreshing transparently when needed.
///
/// The returned token is for SERVER-SIDE use only (e.g. calling HubSpot). It is NEVER returned to the
/// frontend. On an irrecoverable refresh failure the connection is moved to NeedsReconnect and this
/// returns null (the caller should surface "needs reconnect" rather than retry).
/// </summary>
public interface IHubSpotTokenProvider
{
    /// <summary>
    /// Returns a valid decrypted access token for the tenant, or null if there is no usable connection
    /// (no connection, disconnected, or refresh failed irrecoverably).
    /// </summary>
    Task<string?> GetValidAccessTokenAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
