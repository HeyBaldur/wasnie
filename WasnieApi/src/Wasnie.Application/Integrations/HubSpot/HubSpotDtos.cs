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
    DateTimeOffset? DisconnectedAt,
    // Phase 3: when the automatic polling sync last ran successfully for this tenant. Null = never auto-synced.
    DateTimeOffset? LastSyncedAt = null,
    // WI-CRM-CATEGORY: the HubSpot property the tenant declared feeds Category. Null = not configured.
    string? CategoryPropertyName = null,
    // True when the tenant is on Free: every operation below is refused and the auto-sync skips them.
    // Status still reports the truth — a tenant that connected while paying and then downgraded reads
    // "Connected" AND RequiresUpgrade, which is exactly the frozen state the UI has to explain.
    bool RequiresUpgrade = false);

/// <summary>Result of the verification "ping" — non-secret account info proving the token works.</summary>
public sealed record HubSpotPingResultDto(
    long PortalId,
    string? AccountType,
    string? TimeZone,
    string? CompanyCurrency,
    string? UiDomain);

/// <summary>
/// One closed-won deal as read from HubSpot (Phase 2a verification — read-only, nothing is created).
/// </summary>
public sealed record HubSpotDealPreviewItemDto(
    string DealId,
    string? DealName,
    decimal? Amount,
    string? CurrencyCode,
    DateOnly? CloseDate,
    string? OwnerId,
    string? OwnerName,
    string? OwnerEmail);

/// <summary>Preview of the closed-won deals fetched from the connected HubSpot account.</summary>
public sealed record HubSpotDealsPreviewDto(
    int Count,
    IReadOnlyList<HubSpotDealPreviewItemDto> Deals);

/// <summary>
/// Summary of a deal import (FASE 2c). Idempotent: re-running re-reads the same deals but only creates
/// transactions for deals not already imported (<see cref="SkippedAlreadyImported"/>).
///
/// Drift (WI-HUBSPOT-DRIFT): an already-imported deal whose amount/close-date changed in the CRM is
/// reconciled — <see cref="DriftAutoResolved"/> still-Pending deals were re-created with the new values;
/// <see cref="DriftAlertsRaised"/> already-Calculated/Paid deals were left untouched and flagged for review.
/// </summary>
public sealed record HubSpotImportResultDto(
    int DealsRead,
    int Created,
    int AssignedToPayee,
    int Unassigned,
    int SkippedAlreadyImported,
    int SkippedInvalid,
    int NewOwnerMappings,
    int DriftAutoResolved,
    int DriftAlertsRaised,
    int TotalsMismatch,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A HubSpot owner that did NOT auto-resolve to a payee (no existing mapping AND no exact email match),
/// surfaced in the manual mapping queue (FASE 2d). Includes how many of their closed-won deals are already
/// sitting Unassigned, so the user knows how much a link would clean up.
/// </summary>
public sealed record UnresolvedCrmOwnerDto(
    string OwnerId,
    string? Name,
    string? Email,
    bool Archived,
    int ClosedWonDealCount,
    int UnassignedTransactionCount);

/// <summary>The manual mapping queue: owners awaiting a link to a Wasnie payee.</summary>
public sealed record UnresolvedCrmOwnersDto(
    int Count,
    IReadOnlyList<UnresolvedCrmOwnerDto> Owners);

/// <summary>Outcome of manually linking an owner to a payee (FASE 2d).</summary>
public sealed record LinkCrmOwnerResultDto(int ReassignedTransactions);
