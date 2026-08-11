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
    IReadOnlyList<DriftAlertDto> DriftAlerts,
    IReadOnlyList<DealLostAlertDto> DealLostAlerts,
    IReadOnlyList<AmbiguousAttributionPayeeDto> AmbiguousAttributionPayees);

// Transactions blocked because their plan cannot be determined: the payee has 2+ eligible plans and
// nobody said which one applies, so the engine refuses to guess.
//
// Grouped BY PAYEE, not per transaction, because the cause is the payee's overlapping assignments —
// one payee with 43 blocked transactions is ONE problem to fix, not 43. Fixing the cause (usually
// deactivating the assignment that should not apply) unblocks all of them at once, which is why the
// deep-link points at the payee's assignments.
public sealed record AmbiguousAttributionPayeeDto(
    Guid PayeeId,
    string PayeeName,
    string? EmployeeCode,
    int TransactionCount,
    IReadOnlyList<string> PlanNames);

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

// A deal-lost alert: a CRM deal Wasnie already turned into a commission is NO LONGER closed-won (moved to
// Lost or an open stage) after its transaction was Calculated or Paid. Separate from DriftAlertDto (which is
// an amount/date change on a STILL-won deal). Calculated → the UI offers "Revert commission"; Paid →
// informational only (clawback of paid money is out of scope). CommissionAmount is what a revert takes back.
/// <param name="TransactionStatus">
/// The commission's status RIGHT NOW, read from the transaction itself — this is what the screen
/// decides on. It used to be the status recorded when the alert was raised, and a commission paid
/// after detection kept the screen offering "revert (it has not been paid)" over money that had
/// already left the company. The backend refused the revert, but the sentence was false.
/// </param>
/// <param name="StatusAtDetection">
/// The status when the loss was first detected. Kept as history — it explains why the alert exists —
/// but it never drives an action.
/// </param>
/// <param name="ClawbackState">
/// For a PAID commission: whether the churn clawback already produced a debit
/// (<see cref="ClawbackStates.Applied"/>) or is still to come (<see cref="ClawbackStates.Pending"/>).
/// <see cref="ClawbackStates.NotApplicable"/> whenever the commission is not paid — there is nothing
/// to claw back from an unpaid commission; that case is a revert.
/// </param>
public sealed record DealLostAlertDto(
    Guid TransactionId,
    string ReferenceNumber,
    string ExternalDealId,
    string TransactionStatus,
    string StatusAtDetection,
    string ClawbackState,
    decimal CommissionAmount,
    string CommissionCurrency,
    DateTimeOffset DetectedAt);

/// <summary>The vocabulary of <see cref="DealLostAlertDto.ClawbackState"/>. Shared with the client so
/// the screen never re-derives it from an amount or a status.</summary>
public static class ClawbackStates
{
    public const string NotApplicable = "NotApplicable";
    public const string Applied = "Applied";
    public const string Pending = "Pending";
}

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
    IReadOnlyList<DashboardTrendPointDto> CommissionTrend,
    // True when the selected period is still RUNNING. The band then reports PACING — how far the period
    // has got against the previous period's total — instead of a change percentage. Both cases compare
    // against the same window; only the presentation differs.
    bool IsPacing = false,
    // The exact windows the two bars represent, so the UI can drill from either bar down to the payouts
    // that make it up. Sent from here rather than recomputed in the browser: PeriodHelper is the single
    // source of truth for what a period covers, and a second implementation would drift from it.
    DateOnly? CurrentFrom = null,
    DateOnly? CurrentTo = null,
    DateOnly? PriorFrom = null,
    DateOnly? PriorTo = null);

// One row per currency: current amount, prior amount, and either a % change (closed period) or a pacing
// percentage (running period) — never both.
public sealed record DashboardTrendPointDto(
    string Currency,
    decimal CurrentAmount,
    decimal PriorAmount,
    decimal? ChangePercent,   // null if prior = 0 (no meaningful base) — ALWAYS null while pacing
    string Direction,         // "up" | "down" | "neutral" | "pacing"
    // Running periods only: CurrentAmount as a percentage of the previous period's TOTAL. Can exceed 100
    // once the baseline is beaten, which is a good outcome and is rendered as such. Null when the
    // previous period total is zero, since there is no baseline to pace against.
    //
    // Deliberately NOT a change percentage: €500 of August against all €4,939 of July is -89.9%, and
    // showing that as a red down arrow every first of the month reads as a collapse that never happened.
    decimal? PacingPercent = null);

// ── Activity feed ───────────────────────────────────────────────────────────

public sealed record DashboardActivityItemDto(
    DateTime TimestampUtc,
    string ActorEmail,
    string ActorInitials,
    string Action,
    string ResourceType,
    string? ResourceDisplayName);
