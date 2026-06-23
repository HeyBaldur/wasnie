using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Dashboard;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;

namespace Wasnie.Application.Compensation.Handlers.Dashboard;

public sealed class GetDashboardSummaryHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IClock clock)
    : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    private const int ActivityFeedLimit = 10;

    public async Task<Result<DashboardSummaryDto>> Handle(
        GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.ReportsViewAll, cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var period = request.Period;

        var (from, to) = PeriodHelper.ComputeDateRange(period, today);
        var (priorFrom, priorTo) = PeriodHelper.ComputePriorPeriodRange(period, today);
        var periodLabel = PeriodHelper.GetPeriodLabel(period, today);
        var priorLabel = PeriodHelper.GetPriorPeriodLabel(period, today);

        var actionBand = await BuildActionBandAsync(cancellationToken);
        var pendingByPlan = await BuildPendingByPlanAsync(cancellationToken);
        var unprocessablePending = await BuildUnprocessablePendingAsync(cancellationToken);
        actionBand = actionBand with
        {
            PendingByPlanItems = pendingByPlan,
            UnprocessablePendingItems = unprocessablePending,
        };
        var periodBand = await BuildPeriodBandAsync(from, to, cancellationToken);
        var trendBand = BuildTrendBandEnabled(priorFrom, priorTo)
            ? await BuildTrendBandAsync(from, to, priorFrom!.Value, priorTo!.Value, periodLabel, priorLabel, cancellationToken)
            : null;
        var activityFeed = await BuildActivityFeedAsync(cancellationToken);

        return Result<DashboardSummaryDto>.Success(new DashboardSummaryDto(
            PeriodLabel: periodLabel,
            ActionBand: actionBand,
            PeriodBand: periodBand,
            TrendBand: trendBand,
            ActivityFeed: activityFeed));
    }

    // ── Banda 1 — period-independent action items ─────────────────────────────

    private async Task<DashboardActionBandDto> BuildActionBandAsync(CancellationToken ct)
    {
        var draftPayRuns = await db.PayRuns
            .CountAsync(r => r.Status == PayRunStatus.Draft, ct);

        // Payouts pending approval = Status.Calculated, non-zero amount only.
        // Mirrors the payouts list default (hideZero=true / ExcludeZero=true) so that
        // the card count matches what the user sees when they click through to the list.
        var pendingRaw = await db.CompensationPayouts
            .Where(p => p.Status == CompensationPayoutStatus.Calculated
                     && p.TotalCommission.Amount > 0)
            .Select(p => new { p.TotalCommission.Amount, p.TotalCommission.Currency })
            .ToListAsync(ct);

        var pendingCount = pendingRaw.Count;
        var pendingByCurrency = pendingRaw
            .GroupBy(p => p.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(p => p.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        // Approved but not yet paid = Status.Approved, non-zero amount only.
        var approvedUnpaidRaw = await db.CompensationPayouts
            .Where(p => p.Status == CompensationPayoutStatus.Approved
                     && p.TotalCommission.Amount > 0)
            .Select(p => new { p.TotalCommission.Amount, p.TotalCommission.Currency })
            .ToListAsync(ct);

        var approvedUnpaidByCurrency = approvedUnpaidRaw
            .GroupBy(p => p.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(p => p.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        return new DashboardActionBandDto(
            DraftPayRunsCount: draftPayRuns,
            PayoutsPendingApprovalCount: pendingCount,
            PayoutsPendingApprovalByCurrency: pendingByCurrency,
            PayoutsApprovedUnpaidByCurrency: approvedUnpaidByCurrency,
            PendingByPlanItems: [],
            UnprocessablePendingItems: []);
    }

    // ── Pending transactions grouped by plan (action band supplement) ─────────
    // Anti-Cartesian: three separate queries; matching done in-memory.
    // Counts distinct Pending transaction IDs eligible for the ByPlan ProcessPending scope.

    private async Task<IReadOnlyList<PlanPendingCountDto>> BuildPendingByPlanAsync(CancellationToken ct)
    {
        // Load active assignments that have an effective period (full entities — owned type)
        var activeAssignments = await db.PlanAssignments
            .Where(a => a.Status == AssignmentStatus.Active)
            .ToListAsync(ct);

        var withPeriod = activeAssignments.Where(a => a.EffectivePeriod is not null).ToList();
        if (withPeriod.Count == 0) return [];

        // Load plan name + currency for those plans
        var planIds = withPeriod.Select(a => a.PlanId).Distinct().ToList();
        var plans = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Currency })
            .ToDictionaryAsync(p => p.Id, ct);

        // Load all Pending transactions for the relevant payees (one query)
        var payeeIds = withPeriod.Select(a => a.PayeeId).Distinct().ToList();
        var pendingTx = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId.HasValue
                     && payeeIds.Contains(t.PayeeId!.Value))
            .Select(t => new
            {
                t.Id,
                PayeeId = t.PayeeId!.Value,
                t.TransactionDate,
                Currency = t.Amount.Currency,
            })
            .ToListAsync(ct);

        if (pendingTx.Count == 0) return [];

        // Match in-memory: collect distinct Tx IDs per plan (HashSet prevents double-counting
        // when a payee has multiple overlapping assignments to the same plan — rare but possible)
        var txSetByPlan = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var a in withPeriod)
        {
            if (!plans.TryGetValue(a.PlanId, out var plan)) continue;
            var start = a.EffectivePeriod!.Start;
            var end = a.EffectivePeriod.End;
            foreach (var t in pendingTx.Where(t =>
                t.PayeeId == a.PayeeId
                && t.TransactionDate >= start
                && t.TransactionDate <= end
                && t.Currency == plan.Currency))
            {
                if (!txSetByPlan.TryGetValue(a.PlanId, out var set))
                    txSetByPlan[a.PlanId] = set = [];
                set.Add(t.Id);
            }
        }

        return txSetByPlan
            .Where(kvp => kvp.Value.Count > 0)
            .OrderByDescending(kvp => kvp.Value.Count)
            .Select(kvp => new PlanPendingCountDto(
                PlanId: kvp.Key,
                PlanName: plans[kvp.Key].Name,
                Currency: plans[kvp.Key].Currency,
                PendingCount: kvp.Value.Count))
            .ToList();
    }

    // ── Pending transactions that CANNOT be processed yet, grouped by reason ──
    // Visibility supplement (WI): the inverse of BuildPendingByPlanAsync. A Pending transaction is
    // processable iff it has a payee, that payee has an Active assignment whose EffectivePeriod covers
    // the transaction date, AND tx.Currency == that plan's Currency (the rule the engine enforces in
    // ProcessPendingTransactionsJobHandler). Anything Pending that fails this is surfaced here so it is
    // not silently invisible. Period-independent (mirrors the existing panel). Anti-Cartesian: three
    // queries + in-memory matching; no N+1.
    //
    // Primary reason per transaction (mutually exclusive — counted once):
    //   NoPayee            → PayeeId is null
    //   NoActiveAssignment → has payee but NO Active assignment covers the transaction date
    //   CurrencyMismatch   → a covering Active assignment exists, but none of those plans' currency
    //                        matches the transaction currency
    private async Task<IReadOnlyList<UnprocessablePendingDto>> BuildUnprocessablePendingAsync(CancellationToken ct)
    {
        // Counts come straight from the shared spec so they are IDENTICAL to what the Transactions list
        // shows when deep-linked by reason (no drift between the dashboard and the filter).
        var noPayee = await UnprocessablePendingSpec.NoPayee(db).CountAsync(ct);
        var currencyMismatch = await UnprocessablePendingSpec.CurrencyMismatch(db).CountAsync(ct);
        var noActiveAssignment = await UnprocessablePendingSpec.NoActiveAssignment(db).CountAsync(ct);

        var mismatchCurrencies = currencyMismatch > 0
            ? await UnprocessablePendingSpec.CurrencyMismatch(db)
                .Select(t => t.Amount.Currency)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync(ct)
            : [];

        var items = new List<UnprocessablePendingDto>(3);
        if (noPayee > 0)
            items.Add(new UnprocessablePendingDto(UnprocessablePendingSpec.NoPayeeReason, noPayee, []));
        if (currencyMismatch > 0)
            items.Add(new UnprocessablePendingDto(
                UnprocessablePendingSpec.CurrencyMismatchReason, currencyMismatch, mismatchCurrencies));
        if (noActiveAssignment > 0)
            items.Add(new UnprocessablePendingDto(
                UnprocessablePendingSpec.NoActiveAssignmentReason, noActiveAssignment, []));

        return items;
    }

    // ── Banda 2 — period-filtered state ──────────────────────────────────────

    private async Task<DashboardPeriodBandDto> BuildPeriodBandAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        // Transactions
        var txQuery = db.CompensationTransactions
            .Where(t => t.Status != CompensationTransactionStatus.Cancelled);
        if (from.HasValue) txQuery = txQuery.Where(t => t.TransactionDate >= from.Value);
        if (to.HasValue) txQuery = txQuery.Where(t => t.TransactionDate <= to.Value);

        var txCount = await txQuery.CountAsync(ct);
        var txVolumeRaw = await txQuery
            .Select(t => new { t.Amount.Amount, t.Amount.Currency })
            .ToListAsync(ct);
        var txVolume = txVolumeRaw
            .GroupBy(t => t.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(t => t.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        // Payouts by period intersection
        var payoutsInPeriod = await PayoutsInPeriodRawAsync(from, to, ct);
        var payoutsByCurrency = payoutsInPeriod
            .GroupBy(p => p.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(p => p.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        // Credits allocated in period (AllocatedAt)
        var creditsQuery = db.Credits.Where(c => c.SupersededAt == null);
        if (from.HasValue)
        {
            var fromDto = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            creditsQuery = creditsQuery.Where(c => c.AllocatedAt >= fromDto);
        }
        if (to.HasValue)
        {
            var toDto = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            creditsQuery = creditsQuery.Where(c => c.AllocatedAt <= toDto);
        }

        var creditsCount = await creditsQuery.CountAsync(ct);
        var creditsRaw = await creditsQuery
            .Select(c => new { c.CreditedAmount.Amount, c.CreditedAmount.Currency })
            .ToListAsync(ct);
        var creditsByCurrency = creditsRaw
            .GroupBy(c => c.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(c => c.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        // Avg quota attainment — anti-Cartesian: load quotas + credits separately, match in-memory
        var avgAttainment = await ComputeAvgAttainmentAsync(from, to, ct);

        // Active plans (current state, not period-filtered)
        var activePlans = await db.CompensationPlans
            .CountAsync(p => p.Status == PlanStatus.Active, ct);

        // Active quotas (current state)
        var activeQuotas = await db.Quotas
            .CountAsync(q => q.Status == QuotaStatus.Active, ct);

        // Payees: IsActive is the platform eligibility flag; snapshot of current state
        var payeesActive = await db.Payees.CountAsync(p => p.IsActive, ct);
        var payeesInactive = await db.Payees.CountAsync(p => !p.IsActive, ct);

        return new DashboardPeriodBandDto(
            TransactionsCount: txCount,
            TransactionsVolumeByCurrency: txVolume,
            PayoutsTotalByCurrency: payoutsByCurrency,
            CreditsCount: creditsCount,
            CreditsTotalByCurrency: creditsByCurrency,
            AvgQuotaAttainmentPercent: avgAttainment,
            ActivePlansCount: activePlans,
            ActiveQuotasCount: activeQuotas,
            PayeesActiveCount: payeesActive,
            PayeesInactiveCount: payeesInactive);
    }

    // ── Banda 3 — trend (current vs prior) ───────────────────────────────────

    private static bool BuildTrendBandEnabled(DateOnly? priorFrom, DateOnly? priorTo) =>
        priorFrom.HasValue && priorTo.HasValue;

    private async Task<DashboardTrendBandDto> BuildTrendBandAsync(
        DateOnly? from, DateOnly? to,
        DateOnly priorFrom, DateOnly priorTo,
        string currentLabel, string priorLabel,
        CancellationToken ct)
    {
        var currentRaw = await PayoutsInPeriodRawAsync(from, to, ct);
        var priorRaw = await PayoutsInPeriodRawAsync(priorFrom, priorTo, ct);

        var currentByCurrency = currentRaw
            .GroupBy(p => p.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var priorByCurrency = priorRaw
            .GroupBy(p => p.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        // Build trend points for all currencies that appear in either period
        var allCurrencies = currentByCurrency.Keys.Union(priorByCurrency.Keys).OrderBy(c => c).ToList();

        var trendPoints = allCurrencies.Select(currency =>
        {
            var current = currentByCurrency.GetValueOrDefault(currency, 0m);
            var prior = priorByCurrency.GetValueOrDefault(currency, 0m);
            decimal? changePercent = prior == 0m ? null : Math.Round((current - prior) / prior * 100m, 2);
            var direction = changePercent switch
            {
                null => "neutral",
                > 0 => "up",
                < 0 => "down",
                _ => "neutral",
            };
            return new DashboardTrendPointDto(currency, current, prior, changePercent, direction);
        }).ToList();

        return new DashboardTrendBandDto(currentLabel, priorLabel, trendPoints);
    }

    // Shared helper: load payout amounts by currency for a date range (period intersection)
    private async Task<List<(decimal Amount, string Currency)>> PayoutsInPeriodRawAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = db.CompensationPayouts.AsQueryable();
        // Period intersection: payout.Period.End >= from AND payout.Period.Start <= to
        if (from.HasValue) q = q.Where(p => p.Period.End >= from.Value);
        if (to.HasValue) q = q.Where(p => p.Period.Start <= to.Value);

        var raw = await q
            .Select(p => new { p.TotalCommission.Amount, p.TotalCommission.Currency })
            .ToListAsync(ct);

        return raw.Select(p => (p.Amount, p.Currency)).ToList();
    }

    // ── Avg quota attainment — anti-Cartesian ─────────────────────────────────
    // Doc 15 invariant: credits joined to transactions is 1:1 (Credit.TransactionId FK).
    // We load quotas and all relevant credits in two separate queries; matching is in-memory.
    // This avoids any join that could multiply rows across the quota-credit relationship.
    private async Task<decimal?> ComputeAvgAttainmentAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var quotas = await db.Quotas
            .Where(q => q.Status == QuotaStatus.Active)
            .Where(q =>
                (!from.HasValue || q.Period.End >= from.Value) &&
                (!to.HasValue || q.Period.Start <= to.Value))
            .Select(q => new
            {
                q.PayeeId,
                q.PlanId,
                Target = q.Amount.Amount,
                Currency = q.Amount.Currency,
                q.Period.Start,
                q.Period.End,
                q.MeasurementType,
            })
            .ToListAsync(ct);

        if (quotas.Count == 0) return null;

        // Load all non-superseded credits with their transaction dates and amounts in one query.
        // No quota join here — matching is done in-memory below (no Cartesian risk).
        var allCredits = await (
            from c in db.Credits
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            where c.SupersededAt == null
            select new
            {
                c.PayeeId,
                c.PlanId,
                TxAmount = t.Amount.Amount,
                Currency = t.Amount.Currency,
                Quantity = t.Quantity,
                t.TransactionDate,
            }
        ).ToListAsync(ct);

        var attainments = new List<decimal>(quotas.Count);
        foreach (var q in quotas)
        {
            decimal achieved;
            if (q.MeasurementType == QuotaMeasurementType.Units)
            {
                achieved = allCredits
                    .Where(c => c.PayeeId == q.PayeeId && c.PlanId == q.PlanId
                                && c.TransactionDate >= q.Start && c.TransactionDate <= q.End)
                    .Sum(c => (decimal)c.Quantity);
            }
            else
            {
                achieved = allCredits
                    .Where(c => c.PayeeId == q.PayeeId && c.PlanId == q.PlanId
                                && c.Currency == q.Currency
                                && c.TransactionDate >= q.Start && c.TransactionDate <= q.End)
                    .Sum(c => c.TxAmount);
            }

            if (q.Target > 0m)
                attainments.Add(Math.Round(achieved / q.Target * 100m, 4));
        }

        if (attainments.Count == 0) return null;
        return Math.Round(attainments.Sum() / attainments.Count, 2);
    }

    // ── Activity feed from AuditLog ───────────────────────────────────────────

    private async Task<IReadOnlyList<DashboardActivityItemDto>> BuildActivityFeedAsync(CancellationToken ct)
    {
        var logs = await db.AuditLogs
            .OrderByDescending(l => l.TimestampUtc)
            .Take(ActivityFeedLimit)
            .Select(l => new
            {
                l.TimestampUtc,
                l.ActorEmail,
                l.Action,
                l.ResourceType,
                l.ResourceDisplayName,
            })
            .ToListAsync(ct);

        return logs.Select(l => new DashboardActivityItemDto(
            TimestampUtc: l.TimestampUtc,
            ActorEmail: l.ActorEmail,
            ActorInitials: BuildInitials(l.ActorEmail),
            Action: l.Action,
            ResourceType: l.ResourceType,
            ResourceDisplayName: l.ResourceDisplayName))
            .ToList();
    }

    private static string BuildInitials(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : local.Length >= 2 ? local[..2].ToUpperInvariant() : local.ToUpperInvariant();
    }
}
