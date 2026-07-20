namespace Wasnie.Application.Compensation.DTOs;

// ── Top-level response ──────────────────────────────────────────────────────

public sealed record DashboardSummaryDto(
    string PeriodLabel,
    DashboardActionBandDto ActionBand,
    DashboardPeriodBandDto PeriodBand,
    DashboardTrendBandDto? TrendBand,
    IReadOnlyList<DashboardActivityItemDto> ActivityFeed);

// ── Banda 1 — "Requires action" (period-independent) ───────────────────────

public sealed record DashboardActionBandDto(
    int DraftPayRunsCount,
    int PayoutsPendingApprovalCount,
    IReadOnlyList<CurrencyTotalDto> PayoutsPendingApprovalByCurrency,
    IReadOnlyList<CurrencyTotalDto> PayoutsApprovedUnpaidByCurrency,
    IReadOnlyList<PlanPendingCountDto> PendingByPlanItems,
    IReadOnlyList<UnprocessablePendingDto> UnprocessablePendingItems,
    IReadOnlyList<DriftAlertDto> DriftAlerts);

// A CRM drift alert (WI-HubSpot-Drift-Policy): a deal changed in HubSpot (amount and/or close date) AFTER
// its transaction was already Calculated or Paid — so it was NOT auto-corrected (Rule 10, immutable), only
// flagged for review. Distinct from the "unprocessable Pending" reasons: this is money that ALREADY moved
// and whose source deal drifted. ReferenceNumber (HUBSPOT-{dealId}) is the deep-link target; the deal name
// is not persisted on the alert, so the UI shows the deal id / reference.
public sealed record DriftAlertDto(
    Guid TransactionId,
    string ReferenceNumber,
    string ExternalDealId,
    string TransactionStatus,   // "Calculated" | "Paid"
    bool AmountChanged,
    decimal OldAmount,
    string OldCurrency,
    decimal NewAmount,
    string NewCurrency,
    bool DateChanged,
    DateOnly OldCloseDate,
    DateOnly NewCloseDate,
    DateTimeOffset DetectedAt);

// Plans that have Pending transactions eligible for ProcessPending (ByPlan scope)
public sealed record PlanPendingCountDto(
    Guid PlanId,
    string PlanName,
    string Currency,
    int PendingCount);

// Pending transactions that CANNOT be processed yet, grouped by primary reason.
// Reason: "NoPayee" | "CurrencyMismatch" | "NoActiveAssignment". Each transaction is counted once.
// Currencies is populated only for CurrencyMismatch (the distinct currencies involved) so the UI can
// deep-link to Transactions filtered by those currencies; empty for the other reasons.
public sealed record UnprocessablePendingDto(
    string Reason,
    int Count,
    IReadOnlyList<string> Currencies);

// ── Banda 2 — "Period state" ────────────────────────────────────────────────

public sealed record DashboardPeriodBandDto(
    int TransactionsCount,
    IReadOnlyList<CurrencyTotalDto> TransactionsVolumeByCurrency,
    IReadOnlyList<CurrencyTotalDto> PayoutsTotalByCurrency,
    int CreditsCount,
    IReadOnlyList<CurrencyTotalDto> CreditsTotalByCurrency,
    decimal? AvgQuotaAttainmentPercent,  // null = no active quotas in period
    int ActivePlansCount,
    int ActiveQuotasCount,
    int PayeesActiveCount,
    int PayeesInactiveCount);

// ── Banda 3 — Trend (current vs prior period) ──────────────────────────────

public sealed record DashboardTrendBandDto(
    string CurrentPeriodLabel,
    string PriorPeriodLabel,
    IReadOnlyList<DashboardTrendPointDto> CommissionTrend);

// One row per currency: current amount, prior amount, % change
public sealed record DashboardTrendPointDto(
    string Currency,
    decimal CurrentAmount,
    decimal PriorAmount,
    decimal? ChangePercent,   // null if prior = 0 (no meaningful base)
    string Direction);        // "up" | "down" | "neutral"

// ── Activity feed ───────────────────────────────────────────────────────────

public sealed record DashboardActivityItemDto(
    DateTime TimestampUtc,
    string ActorEmail,
    string ActorInitials,
    string Action,
    string ResourceType,
    string? ResourceDisplayName);
