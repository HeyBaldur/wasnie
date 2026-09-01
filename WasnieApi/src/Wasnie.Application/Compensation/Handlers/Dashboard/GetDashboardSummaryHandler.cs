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
        // The band always compares against the WHOLE previous period now, so the plain period names are
        // exact for both sides — no range labels needed, and "July 2026 = €4,939.41" agrees with what the
        // Last Month screen reports for July.
        var priorLabel = PeriodHelper.GetPriorPeriodLabel(period, today);
        // Presentation switch only: running → pacing bar, closed → change percentage.
        var isPacing = PeriodHelper.IsRunningPeriod(period, today);

        var actionBand = await BuildActionBandAsync(cancellationToken);
        var pendingByPlan = await BuildPendingByPlanAsync(cancellationToken);
        var unprocessablePending = await BuildUnprocessablePendingAsync(cancellationToken);
        var driftAlerts = await BuildDriftAlertsAsync(cancellationToken);
        var dealLostAlerts = await BuildDealLostAlertsAsync(cancellationToken);
        var ambiguousAttribution = await BuildAmbiguousAttributionAsync(cancellationToken);
        actionBand = actionBand with
        {
            PendingByPlanItems = pendingByPlan,
            UnprocessablePendingItems = unprocessablePending,
            DriftAlerts = driftAlerts,
            DealLostAlerts = dealLostAlerts,
            AmbiguousAttributionPayees = ambiguousAttribution,
        };
        var periodBand = await BuildPeriodBandAsync(from, to, cancellationToken);
        var trendBand = BuildTrendBandEnabled(priorFrom, priorTo)
            ? await BuildTrendBandAsync(from, to, priorFrom!.Value, priorTo!.Value, periodLabel, priorLabel, isPacing, cancellationToken)
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
            UnprocessablePendingItems: [],
            DriftAlerts: [],
            DealLostAlerts: [],
            AmbiguousAttributionPayees: [],
            PlansWithoutLiveRules: await BuildPlansWithoutLiveRulesAsync(ct));
    }

    // ── Active plans that have stopped paying ────────────────────────────────────────────────
    // Every rule stopped, plan still Active, sales still arriving. DERIVED from the rules each time
    // rather than read from a flag: this warning is wrong in both directions if it goes stale.
    private async Task<IReadOnlyList<PlanWithoutLiveRulesDto>> BuildPlansWithoutLiveRulesAsync(CancellationToken ct)
    {
        // A plan that never had a rule is not this: it cannot be Active (Plan.Activate demands one),
        // and if one ever existed the rows are still there. `Any(IsActive)` is the engine's own
        // predicate — the same one CreditAllocationService filters on — so the card cannot disagree
        // with what the engine actually does.
        var candidates = await db.CompensationPlans
            .Where(p => p.Status == PlanStatus.Active
                     && p.Rules.Any()
                     && !p.Rules.Any(r => r.IsActive))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Version,
                // The last brake pulled is when this plan stopped paying.
                StoppedAt = p.Rules.Max(r => r.StoppedAt),
            })
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        var planIds = candidates.Select(c => c.Id).ToList();

        // PlanAssignment is a separate aggregate, so the count is a query, not a navigation. It is
        // the number that makes the warning urgent: people are still assigned to a plan paying zero.
        var assignmentCounts = await db.PlanAssignments
            .Where(a => planIds.Contains(a.PlanId) && a.Status == AssignmentStatus.Active)
            .GroupBy(a => a.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, ct);

        return candidates
            .Select(c => new PlanWithoutLiveRulesDto(
                c.Id, c.Name, c.Version, c.StoppedAt,
                assignmentCounts.TryGetValue(c.Id, out var n) ? n : 0))
            .OrderByDescending(p => p.ActiveAssignmentCount)
            .ThenBy(p => p.PlanName)
            .ToList();
    }

    // ── Pending transactions whose plan cannot be determined, grouped BY PAYEE ────────────────
    // A payee on 2+ eligible plans with no declared choice: the engine refuses to guess (see
    // AmbiguousAttributionSpec), so these sit Pending until someone resolves the overlap. Grouped by
    // payee because that is the unit of the FIX — one payee's overlapping assignments cause all of
    // their blocked transactions, and deactivating the surplus assignment unblocks them together.
    //
    // Anti-Cartesian, no N+1: three bounded queries (pending transactions → their payees' assignments
    // → those plans) and all matching in memory through the engine's own Candidates rule.
    private async Task<IReadOnlyList<AmbiguousAttributionPayeeDto>> BuildAmbiguousAttributionAsync(
        CancellationToken ct)
    {
        // Only rows that could possibly be ambiguous: Pending, with a payee, without a declared plan.
        // Projected to the three fields the rule needs — a tenant can have tens of thousands of Pending
        // rows and the dashboard must not materialise them as entities.
        var pending = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId != null
                     && t.SelectedPlanAssignmentId == null)
            .Select(t => new
            {
                PayeeId = t.PayeeId!.Value,
                t.TransactionDate,
                Currency = t.Amount.Currency,
            })
            .ToListAsync(ct);

        if (pending.Count == 0) return [];

        var payeeIds = pending.Select(t => t.PayeeId).Distinct().ToList();

        // ALL assignments for those payees (any status) — Candidates applies the status/period filter.
        var assignments = await db.PlanAssignments
            .Where(a => payeeIds.Contains(a.PayeeId))
            .ToListAsync(ct);

        if (assignments.Count == 0) return [];

        var assignmentsByPayee = assignments
            .GroupBy(a => a.PayeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PlanAssignment>)g.ToList());

        var planIds = assignments.Select(a => a.PlanId).Distinct().ToList();
        var plans = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Currency, p.Status })
            .ToDictionaryAsync(p => p.Id, ct);

        var planCurrencyById = plans.ToDictionary(kv => kv.Key, kv => kv.Value.Currency);
        // An archived plan is not an eligible alternative, so it cannot make an attribution ambiguous.
        var archivedPlanIds = plans
            .Where(kv => kv.Value.Status == PlanStatus.Archived)
            .Select(kv => kv.Key)
            .ToHashSet();

        // Count per payee, and collect the DISTINCT plans that made each one ambiguous — that list is
        // what tells the admin which overlap to look at.
        var countByPayee = new Dictionary<Guid, int>();
        var planNamesByPayee = new Dictionary<Guid, HashSet<string>>();

        foreach (var tx in pending)
        {
            if (!assignmentsByPayee.TryGetValue(tx.PayeeId, out var payeeAssignments)) continue;

            // The projection already excluded rows with a declared plan, hence the null selection.
            var candidates = AmbiguousAttributionSpec.AmbiguousCandidates(
                selectedPlanAssignmentId: null, tx.TransactionDate, tx.Currency,
                payeeAssignments, planCurrencyById, archivedPlanIds);
            if (candidates.Count == 0) continue;

            var payeeId = tx.PayeeId;
            countByPayee.TryGetValue(payeeId, out var existing);
            countByPayee[payeeId] = existing + 1;

            if (!planNamesByPayee.TryGetValue(payeeId, out var names))
                planNamesByPayee[payeeId] = names = [];
            foreach (var c in candidates)
                if (plans.TryGetValue(c.PlanId, out var plan))
                    names.Add(plan.Name);
        }

        if (countByPayee.Count == 0) return [];

        var affectedPayeeIds = countByPayee.Keys.ToList();
        var payees = await db.Payees
            .Where(p => affectedPayeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToDictionaryAsync(p => p.Id, ct);

        return countByPayee
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp =>
            {
                var payee = payees.GetValueOrDefault(kvp.Key);
                return new AmbiguousAttributionPayeeDto(
                    PayeeId: kvp.Key,
                    PayeeName: payee?.FullName ?? string.Empty,
                    EmployeeCode: payee?.EmployeeCode,
                    TransactionCount: kvp.Value,
                    PlanNames: planNamesByPayee[kvp.Key].OrderBy(n => n).ToList());
            })
            .ToList();
    }

    // ── CRM drift alerts (WI-HubSpot-Drift-Policy, PASO 3) ─────────────────────
    // Unresolved alerts for deals that changed in the CRM AFTER their transaction was Calculated/Paid.
    // Read-only surfacing — nothing here resolves them. Tenant-scoped via the global query filter (Rule 9).
    // Capped: these are exceptional; if a tenant somehow has many, show the most recent and the count badge
    // reflects what's shown (a "resolve/acknowledge" flow is a possible future WI).
    private async Task<IReadOnlyList<DriftAlertDto>> BuildDriftAlertsAsync(CancellationToken ct)
    {
        const int cap = 20;
        var rows = await db.CrmDriftAlerts
            .Where(a => a.ResolvedAt == null)
            .OrderByDescending(a => a.DetectedAt)
            .Take(cap)
            .ToListAsync(ct);

        return rows.Select(a => new DriftAlertDto(
            TransactionId: a.TransactionId,
            ReferenceNumber: a.ReferenceNumber,
            ExternalDealId: a.ExternalDealId,
            TransactionStatus: a.TransactionStatus.ToString(),
            AmountChanged: a.AmountChanged,
            OldAmount: a.OldAmount,
            OldCurrency: a.OldCurrency,
            NewAmount: a.NewAmount,
            NewCurrency: a.NewCurrency,
            DateChanged: a.DateChanged,
            OldCloseDate: a.OldCloseDate,
            NewCloseDate: a.NewCloseDate,
            DetectedAt: a.DetectedAt)).ToList();
    }

    // ── Deal-lost alerts (reverse reconciliation) ──────────────────────────────
    // Unresolved alerts for deals that left closed-won AFTER their transaction was Calculated/Paid. Read-only
    // surfacing; the revert action lives on its own endpoint. Tenant-scoped via the global filter (Rule 9).
    private async Task<IReadOnlyList<DealLostAlertDto>> BuildDealLostAlertsAsync(CancellationToken ct)
    {
        const int cap = 20;

        // JOIN to the transaction, deliberately. The alert stores the status it saw at detection, and a
        // commission paid AFTERWARDS left that snapshot claiming "not paid" — the screen then offered to
        // revert money that had already gone out. The snapshot stays (it is history); the decision moves
        // to the live row. One join, no extra round trip.
        var rows = await (
            from a in db.DealLostAlerts
            join t in db.CompensationTransactions on a.TransactionId equals t.Id
            where a.ResolvedAt == null
            orderby a.DetectedAt descending
            select new
            {
                a.TransactionId,
                a.ReferenceNumber,
                a.ExternalDealId,
                StatusAtDetection = a.TransactionStatus,
                LiveStatus = t.Status,
                a.CommissionAmount,
                a.CommissionCurrency,
                a.DetectedAt,
                // Has the churn trigger already booked the debt for this transaction? Answered from the
                // ledger, which is where the debt actually lives — not inferred from the alert.
                ClawbackApplied = db.PayeeLedgerEntries.Any(
                    e => e.SourceTransactionId == a.TransactionId
                      && e.SourceType == LedgerSourceType.DealChurn),
            })
            .Take(cap)
            .ToListAsync(ct);

        return rows.Select(a => new DealLostAlertDto(
            TransactionId: a.TransactionId,
            ReferenceNumber: a.ReferenceNumber,
            ExternalDealId: a.ExternalDealId,
            TransactionStatus: a.LiveStatus.ToString(),
            StatusAtDetection: a.StatusAtDetection.ToString(),
            // Only a PAID commission can be clawed back; an unpaid one is reverted instead. Any other
            // status (a commission cancelled or returned to Pending while its alert stayed open) claims
            // nothing at all — the screen shows the live status and offers no action.
            ClawbackState: a.LiveStatus != CompensationTransactionStatus.Paid
                ? ClawbackStates.NotApplicable
                : a.ClawbackApplied ? ClawbackStates.Applied : ClawbackStates.Pending,
            CommissionAmount: a.CommissionAmount,
            CommissionCurrency: a.CommissionCurrency,
            DetectedAt: a.DetectedAt)).ToList();
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

        // Payouts attributed to the period where their cycle closes (see PayoutsInPeriodRawAsync)
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
        bool isPacing,
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

            // RUNNING period → pacing. ChangePercent stays null and Direction is "pacing", so nothing
            // downstream can derive a red down arrow from a period that has simply not finished yet.
            // Comparing a few days of the current period against the whole previous one as a change
            // percentage is what printed -89.9% in red every first of the month.
            if (isPacing)
            {
                decimal? pacingPercent = prior <= 0m
                    ? null                                              // no baseline to pace against
                    : Math.Round(current / prior * 100m, 2);            // may exceed 100 — baseline beaten
                return new DashboardTrendPointDto(currency, current, prior, null, "pacing", pacingPercent);
            }

            // CLOSED period → unchanged behaviour: like-for-like change percentage.
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

        return new DashboardTrendBandDto(
            currentLabel, priorLabel, trendPoints, isPacing,
            CurrentFrom: from, CurrentTo: to, PriorFrom: priorFrom, PriorTo: priorTo);
    }

    // Shared helper: load payout amounts by currency attributed to a date range.
    //
    // MONEY RULE — this widget is CASH FLOW (treasury), not accrued commission. Two consequences,
    // both deliberate:
    //
    //   1. Status == Paid, strictly. A Calculated or Approved payout is money the company still owes;
    //      it has not left the account and must not appear in a cash-flow total. (This used to sum
    //      every status, so the figure was neither cash nor accrual.)
    //
    //   2. Attribution by PaidAt — the day the money actually moved — NOT by Period.End (the cycle
    //      close) and NOT by the underlying transaction dates:
    //        · Period.End sends a payout to an arbitrary month. A 2026-01-01→2026-12-31 run whose
    //          money moved in July was reported in December, and July showed €0 despite €2,000 paid.
    //          A quarterly Apr–Jun run put April and May money into June, inflating it by €166K.
    //        · Transaction dates would make already-published months MUTATE whenever a back-dated
    //          transaction arrives. A closed month must never change.
    //      PaidAt is stamped once, at the transition into Paid, and never moved (see CompensationPayout).
    //
    // Do NOT switch this to period intersection (Period.End >= from AND Period.Start <= to): a payout
    // spanning several months then gets counted IN FULL in every month it touches, so the same euros
    // are reported more than once — that is what once made the Banda 3 trend show a permanent 0% change.
    // Intersection remains correct for the overlap guards; it is wrong when summing into a period total.
    private async Task<List<(decimal Amount, string Currency)>> PayoutsInPeriodRawAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = db.CompensationPayouts
            .Where(p => p.Status == CompensationPayoutStatus.Paid && p.PaidAt != null);

        // Attribution by payment date: from <= payout.PaidAt <= to. The range is inclusive of the whole
        // end day — PaidAt is an instant, so the upper bound must be the last tick of `to`, otherwise
        // every payment made after midnight on the final day of the period silently disappears.
        if (from.HasValue)
        {
            var fromDto = new DateTimeOffset(
                from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);
            q = q.Where(p => p.PaidAt >= fromDto);
        }
        if (to.HasValue)
        {
            var toDto = new DateTimeOffset(
                to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero);
            q = q.Where(p => p.PaidAt <= toDto);
        }

        var raw = await q
            .Select(p => new { p.TotalCommission.Amount, p.TotalCommission.Currency })
            .ToListAsync(ct);

        return raw.Select(p => (p.Amount, p.Currency)).ToList();
    }

    // ── Avg quota attainment ──────────────────────────────────────────────────
    // Achieved is the same DISTINCT-sale definition as QuotaAchievedQuery (the shared source of truth):
    // Step 3 lets several rules credit one sale, so summing credits double-counts. That canonical query is
    // per-(payee,plan) and would be an N+1 here — this handler iterates EVERY active quota in the tenant —
    // so we keep the single bulk load and dedup in-memory by transaction id (GroupBy TxId → count each sale
    // once). If the dedup rule ever changes, change QuotaAchievedQuery AND this block together.
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

        // Load all non-superseded credits with their transaction id/date/amount in one query. TxId lets us
        // dedup below so a multi-rule sale counts once. No quota join here (matching is in-memory).
        var allCredits = await (
            from c in db.Credits
            join t in db.CompensationTransactions on c.TransactionId equals t.Id
            where c.SupersededAt == null
            select new
            {
                c.PayeeId,
                c.PlanId,
                TxId = t.Id,
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
                    .GroupBy(c => c.TxId)
                    .Sum(g => (decimal)g.First().Quantity);
            }
            else
            {
                achieved = allCredits
                    .Where(c => c.PayeeId == q.PayeeId && c.PlanId == q.PlanId
                                && c.Currency == q.Currency
                                && c.TransactionDate >= q.Start && c.TransactionDate <= q.End)
                    .GroupBy(c => c.TxId)
                    .Sum(g => g.First().TxAmount);
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
