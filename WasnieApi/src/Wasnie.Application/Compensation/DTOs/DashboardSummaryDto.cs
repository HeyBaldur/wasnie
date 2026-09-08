namespace Wasnie.Application.Compensation.DTOs;

// ── Top-level response ──────────────────────────────────────────────────────

/// <param name="From">The range actually applied, echoed back so the screen labels what it is showing
/// with the window the server used rather than the one the browser believes it asked for.</param>
public sealed record DashboardSummaryDto(
    DateOnly From,
    DateOnly To,
    DashboardActionBandDto ActionBand,
    DashboardPeriodBandDto PeriodBand,
    DashboardCommissionsBandDto CommissionsBand,
    DashboardTrendBandDto? TrendBand,
    IReadOnlyList<DashboardActivityItemDto> ActivityFeed);

// ── Commissions band — Total / Paid / Unpaid over the selected range ────────

/// <summary>
/// The three commission figures, per currency, for the selected range.
///
/// ★★ ONE QUERY, PARTITIONED — NEVER THREE QUERIES. <c>Total = Paid + Unpaid</c> has to hold to the
/// cent, and three independent aggregations over the same table are three chances to drift: one of
/// them acquires a filter the others do not, and the dashboard starts showing money that does not add
/// up. The handler reads the credits of the range ONCE and splits that single set, so the invariant is
/// a property of the code rather than something a test has to keep watching.
///
/// ★★ CLOSED CREDITS ARE NOT IN ANY OF THE THREE. A credit can end a third way — written off, or
/// settled outside Wasnie through payroll (<c>CreditClosureReason</c>) — and that ending is neither
/// paid nor outstanding. Counting it as Unpaid would tell the reader the company still owes money it
/// has decided not to pay; counting it as Paid would claim a write-off as a payment. Both are lies
/// about money, so the three cards describe the PAYABLE cycle and <see cref="ClosedTotalByCurrency"/>
/// carries the remainder separately, for a reader who needs the full generated figure.
/// </summary>
/// <param name="TotalByCurrency">Paid + Unpaid, per currency. Never includes closed credits.</param>
/// <param name="ClosedTotalByCurrency">
/// Credits of the range that left circulation without a payout. Reported so the omission above is
/// visible rather than silent — a figure nobody can see is indistinguishable from money that vanished
/// (§B1).
/// </param>
public sealed record DashboardCommissionsBandDto(
    IReadOnlyList<CurrencyTotalDto> TotalByCurrency,
    IReadOnlyList<CurrencyTotalDto> PaidByCurrency,
    IReadOnlyList<CurrencyTotalDto> UnpaidByCurrency,
    IReadOnlyList<CurrencyTotalDto> ClosedTotalByCurrency);

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
    IReadOnlyList<AmbiguousAttributionPayeeDto> AmbiguousAttributionPayees,
    IReadOnlyList<PlanWithoutLiveRulesDto> PlansWithoutLiveRules);

/// <summary>
/// An ACTIVE plan with no rule left in effect — every one of its rules was stopped.
///
/// ★★ THE STATE THAT IS OTHERWISE SILENT. The plan keeps ingesting sales (a sale happened, so it is
/// recorded whatever the configuration says) and pays nothing on any of them. Nothing else on any
/// screen says so: the plan looks Active, its assignments look fine, and the money simply stops.
/// Someone pulled an emergency brake and the next step — clone, correct, activate — is theirs to
/// take, so it belongs on the surface they open every morning.
///
/// ★ DERIVED, NEVER STORED. Computed from the rules on every read. A stored flag drifts the moment a
/// rule is added or a version activated, and a warning that appears over a plan paying perfectly
/// well is as damaging as one missing from a plan that is not.
/// </summary>
public sealed record PlanWithoutLiveRulesDto(
    Guid PlanId,
    string PlanName,
    int Version,
    /// <summary>When the LAST live rule was stopped — the moment this plan stopped paying.</summary>
    DateTimeOffset? StoppedAt,
    /// <summary>How many assignments are still pointed at a plan that pays nothing.</summary>
    int ActiveAssignmentCount);

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

/// <remarks>
/// The period LABELS used to travel from here as English prose built by <c>PeriodHelper</c>. They are
/// gone: a free range has no name to translate, and the four dates below already say exactly what the
/// two bars cover, so the screen renders them in the reader's own locale (§C1).
/// </remarks>
public sealed record DashboardTrendBandDto(
    IReadOnlyList<DashboardTrendPointDto> CommissionTrend,
    // True when the selected period is still RUNNING. The band then reports PACING — how far the period
    // has got against the previous period's total — instead of a change percentage. Both cases compare
    // against the same window; only the presentation differs.
    // The exact windows the two bars represent, so the UI can drill from either bar down to the payouts
    // that make it up, and can label both in the reader's locale. The PRIOR window is computed on the
    // server (DashboardRangeHelper.PriorRange) and never re-derived in the browser: a second
    // implementation of "the same length, immediately before" would drift from this one.
    // No longer nullable — a range always has a predecessor, unlike "all-time", which the dashboard
    // no longer offers.
    DateOnly CurrentFrom,
    DateOnly CurrentTo,
    DateOnly PriorFrom,
    DateOnly PriorTo,
    // True when the selected range is still RUNNING. The band then reports PACING — how far the range
    // has got against the previous window's total — instead of a change percentage. Both cases compare
    // against the same window; only the presentation differs.
    bool IsPacing = false);

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

/// <summary>
/// One line of the dashboard's Recent Activity feed.
///
/// ★★ Action IS A CODE AND THE SCREEN TRANSLATES IT (§C1/§C2). It used to reach the browser and be
/// turned into English there — `raw.replace(/_/g,' ')` truncated to three words — so
/// `PLAN_CLAWBACK_POLICY_CHANGED` was shown as "plan clawback policy" and `CRM_DRIFT_AUTO_RESOLVED`
/// as "crm drift auto": the verb, which carries the whole meaning, was the part that got cut. The
/// code travels; the words come from the front's whitelist.
///
/// ★ ResourceId TRAVELS SO THE ENTRY CAN LINK TO WHAT IT CHANGED. Without it the feed could name a
/// plan and offer no way to reach it, and the reader's next question — "which plan?" — had no answer
/// on the page. Whether a given ResourceType HAS a screen is the front's decision: a type with no
/// route renders as plain text rather than as a link that goes nowhere.
/// </summary>
public sealed record DashboardActivityItemDto(
    DateTime TimestampUtc,
    string ActorEmail,
    string ActorInitials,
    string Action,
    string ResourceType,
    string ResourceId,
    string? ResourceDisplayName);
