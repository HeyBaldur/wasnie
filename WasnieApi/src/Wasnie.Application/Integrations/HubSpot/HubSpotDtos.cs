namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>Returned by "start connection" — the HubSpot URL the browser should be sent to. No secrets.</summary>
public sealed record HubSpotConnectResultDto(string AuthorizationUrl);

/// <summary>
/// Connection status for the UI. Deliberately contains NO tokens — only safe, displayable fields.
/// A null/absent connection (never connected) is represented by Status = "NeverConnected".
/// </summary>
public sealed record HubSpotConnectionStatusDto(
    string Status,
    long? PortalId,
    string? StatusReason,
    DateTimeOffset? ConnectedAt,
    string? ConnectedBy,
    DateTimeOffset? DisconnectedAt);

/// <summary>Result of the verification "ping" — non-secret account info proving the token works.</summary>
public sealed record HubSpotPingResultDto(
    long PortalId,
    string? AccountType,
    string? TimeZone,
    string? CompanyCurrency,
    string? UiDomain);
