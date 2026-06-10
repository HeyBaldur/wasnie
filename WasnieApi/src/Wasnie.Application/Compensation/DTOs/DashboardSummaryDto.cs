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
    IReadOnlyList<CurrencyTotalDto> PayoutsApprovedUnpaidByCurrency);

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
