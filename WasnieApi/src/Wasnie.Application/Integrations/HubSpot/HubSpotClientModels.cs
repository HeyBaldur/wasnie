namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>Tokens returned by a successful code-exchange or refresh. Treated as secrets.</summary>
public sealed record HubSpotTokenResult(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds);

/// <summary>
/// Outcome of a refresh attempt. <see cref="IsInvalidRefreshToken"/> is true when HubSpot reports the
/// refresh token is no longer valid (e.g. BAD_REFRESH_TOKEN / revoked) — an irrecoverable state that
/// must move the connection to NeedsReconnect (do NOT retry in a loop).
/// </summary>
public sealed record HubSpotRefreshResult(
    bool Success,
    bool IsInvalidRefreshToken,
    HubSpotTokenResult? Tokens,
    string? Error)
{
    public static HubSpotRefreshResult Ok(HubSpotTokenResult tokens) => new(true, false, tokens, null);
    public static HubSpotRefreshResult Invalid(string error) => new(false, true, null, error);
    public static HubSpotRefreshResult TransientFailure(string error) => new(false, false, null, error);
}

/// <summary>Non-secret account details used to verify a connection (the "ping"). Never includes tokens.</summary>
public sealed record HubSpotAccountInfo(
    long PortalId,
    string? AccountType,
    string? TimeZone,
    string? CompanyCurrency,
    string? UiDomain);
