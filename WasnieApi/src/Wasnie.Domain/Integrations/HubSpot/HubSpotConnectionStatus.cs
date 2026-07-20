namespace Wasnie.Domain.Integrations.HubSpot;

/// <summary>
/// Lifecycle status of a tenant's HubSpot connection.
/// NOTE: "NeverConnected" is NOT a value here — the ABSENCE of a row for a tenant means it never connected.
/// NOTE: a "Paused" state (keep tokens, pause sync) is deliberately NOT included in Phase 1 — there is no
/// sync to pause yet (polling is Phase 3). It will be added with the sync fields when Phase 3 lands.
/// </summary>
public enum HubSpotConnectionStatus
{
    /// <summary>Tokens are valid (or refreshable); the connection is usable.</summary>
    Connected = 0,

    /// <summary>Refresh failed irrecoverably (e.g. BAD_REFRESH_TOKEN, revoked access). User must reconnect.</summary>
    NeedsReconnect = 1,

    /// <summary>The user explicitly disconnected. Tokens are cleared; the row is kept for audit.</summary>
    Disconnected = 2,
}
