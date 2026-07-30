using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;
using PlanStatus = Wasnie.Domain.Compensation.Plans.PlanStatus;

namespace Wasnie.Infrastructure.Compensation.Calculation;

public sealed class CreditAllocationService : ICreditAllocationService
{
    private readonly IApplicationDbContext _db;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;
    private readonly ILogger<CreditAllocationService> _logger;
    private readonly IQuotaAttainmentService _quotaAttainmentService;

    public CreditAllocationService(
        IApplicationDbContext db,
        IGuidGenerator guidGenerator,
        IClock clock,
        ILogger<CreditAllocationService> logger,
        IQuotaAttainmentService quotaAttainmentService)
    {
        _db = db;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _logger = logger;
        _quotaAttainmentService = quotaAttainmentService;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        CancellationToken ct = default)
    {
        // Decision #44: unassigned transactions never produce Credits.
        if (transaction.PayeeId == null) return Array.Empty<Credit>();

        // Only Pending transactions get processed.
        if (transaction.Status != CompensationTransactionStatus.Pending)
            return Array.Empty<Credit>();

        // Extract scalar values to avoid EF Core parameter-extraction issues with complex domain objects.
        var tenantId = transaction.TenantId;
        var payeeIdVal = transaction.PayeeId.Value;
        var txDate = transaction.TransactionDate;

        // Decision #40: find the single active PlanAssignment covering TransactionDate.
        // Load all assignments for this payee in-memory then filter by status and date.
        // EF Core 8 + SQL Server does not reliably translate DateOnly comparisons on
        // owned-type properties (DateRange.Start/End) directly in WHERE clauses.
        // A payee typically has very few assignments so this is safe.
        // Use IgnoreQueryFilters + explicit TenantId check to avoid nested global filter evaluation.
        var allPayeeAssignments = await _db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(pa =>
                pa.TenantId == tenantId &&
                pa.PayeeId == payeeIdVal)
            .ToListAsync(ct);

        // Pattern B: load plan currencies so the resolver can match by currency.
        var assignmentPlanIds = allPayeeAssignments.Select(a => a.PlanId).Distinct().ToList();
        // Status travels with the currency: eligibility needs both, and loading one without the other
        // is what let a retired plan keep paying.
        var planFacts = assignmentPlanIds.Count > 0
            ? await _db.CompensationPlans
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && assignmentPlanIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Currency, p.Status })
                .ToListAsync(ct)
            : [];

        var planCurrencyById = planFacts.ToDictionary(p => p.Id, p => p.Currency);
        var archivedPlanIds = planFacts
            .Where(p => p.Status == PlanStatus.Archived)
            .Select(p => p.Id)
            .ToHashSet();

        // Every eligible assignment contributes its rules — an explicit selection restricts to one.
        var assignments = ResolveAssignments(
            transaction, allPayeeAssignments, txDate, transaction.Amount.Currency,
            planCurrencyById, archivedPlanIds);

        if (assignments.Count == 0) return Array.Empty<Credit>();

        // Load the Plans with rules eagerly in ONE query for all resolved assignments — Rules is a
        // related collection, not auto-loaded, and querying per assignment would be an N+1.
        var resolvedPlanIds = assignments.Select(a => a.PlanId).Distinct().ToList();
        var plansById = await _db.CompensationPlans
            .IgnoreQueryFilters()
            .Include(p => p.Rules)
            .Where(p => resolvedPlanIds.Contains(p.Id) && p.TenantId == tenantId)
            .ToDictionaryAsync(p => p.Id, ct);

        // Single-transaction path: load this transaction's live credit keys itself. One bounded query
        // for one transaction — the N+1 concern that keeps this out of BuildCreditsAsync applies to
        // the batch path, which loads the whole chunk's keys up front instead.
        var alreadyCredited = await LoadLiveCreditKeysAsync([transaction.Id], ct);

        return await BuildCreditsForAllAsync(
            transaction, assignments,
            planId => plansById.GetValueOrDefault(planId),
            alreadyCredited, ct);
    }

    /// <summary>
    /// The (TransactionId, PlanId, RuleId) triples that currently hold a live (non-superseded) credit.
    /// Superseded rows are excluded so RecalculateCredits can supersede and re-create the same key —
    /// mirrors the filter of UX_Credits_Tenant_Transaction_Plan_Rule_Live exactly. A CONSUMED credit is
    /// still live here: it is precisely the one a duplicate must never be created against.
    /// </summary>
    public async Task<IReadOnlySet<(Guid TransactionId, Guid PlanId, Guid RuleId)>> LoadLiveCreditKeysAsync(
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken ct = default)
    {
        if (transactionIds.Count == 0)
            return new HashSet<(Guid, Guid, Guid)>();

        var rows = await _db.Credits
            .Where(c => c.SupersededAt == null && transactionIds.Contains(c.TransactionId))
            .Select(c => new { c.TransactionId, c.PlanId, c.RuleId })
            .ToListAsync(ct);

        return rows.Select(r => (r.TransactionId, r.PlanId, r.RuleId)).ToHashSet();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlanAssignment>> assignmentsByPayee,
        IReadOnlyDictionary<Guid, CompensationPlan> plansById,
        IReadOnlySet<(Guid TransactionId, Guid PlanId, Guid RuleId)>? alreadyCredited = null,
        CancellationToken ct = default)
    {
        // Decision #44: unassigned transactions never produce Credits.
        if (transaction.PayeeId == null)
            return Array.Empty<Credit>();

        // Only Pending transactions get processed.
        if (transaction.Status != CompensationTransactionStatus.Pending)
            return Array.Empty<Credit>();

        var payeeIdVal = transaction.PayeeId.Value;
        var txDate = transaction.TransactionDate;

        // Resolve assignment from the caller-supplied pre-loaded dictionary (no DB query).
        if (!assignmentsByPayee.TryGetValue(payeeIdVal, out var payeeAssignments))
            return Array.Empty<Credit>();

        // Pattern B: build both eligibility lookups from the pre-loaded plans dictionary (no DB query).
        // The batch caller hands over whole Plan entities, so the archived set costs nothing extra —
        // and the batch path is the one the CRM sync runs on, which is where the mis-attribution of
        // 2026-07-22 actually happened.
        var planCurrencyById = plansById.ToDictionary(kv => kv.Key, kv => kv.Value.Currency);
        var archivedPlanIds = plansById
            .Where(kv => kv.Value.Status == PlanStatus.Archived)
            .Select(kv => kv.Key)
            .ToHashSet();

        // Every eligible assignment contributes its rules — an explicit selection restricts to one.
        var assignments = ResolveAssignments(
            transaction, payeeAssignments, txDate, transaction.Amount.Currency,
            planCurrencyById, archivedPlanIds);

        // Plans come from the caller-supplied dictionary — no DB query per assignment (no N+1).
        return await BuildCreditsForAllAsync(
            transaction, assignments,
            planId => plansById.GetValueOrDefault(planId),
            alreadyCredited ?? new HashSet<(Guid, Guid, Guid)>(), ct);
    }

    /// <summary>
    /// EVERY assignment that should contribute rules to this transaction, shared by both overloads.
    ///
    /// This used to return a single assignment picked by tie-break (shortest period, then smallest Id),
    /// so the rules of every other eligible plan were never evaluated. That is the inverse of how
    /// incentive compensation works: transactions are not routed to a plan — every rule in force reads
    /// the transaction and decides for itself. Two rules matching one sale must BOTH credit (a base
    /// plan and a stacked SPIFF); a plan that should not apply expresses that in its own filters, not
    /// by losing a tie-break.
    ///
    /// An explicit admin selection still wins, and now means "restrict to exactly this assignment" —
    /// a pin, not a hint. If it stopped being eligible this throws, so the caller's existing skip
    /// machinery records a readable reason instead of silently crediting somewhere else.
    ///
    /// Ordering is deterministic (narrowest period, then Id — the old tie-break order) purely so runs
    /// are reproducible. It carries no meaning now that every candidate is used: attainment is scoped
    /// per plan (QuotaAttainmentService filters on PlanId), so no assignment can influence another's
    /// numbers regardless of the order they are processed in.
    /// </summary>
    private static IReadOnlyList<PlanAssignment> ResolveAssignments(
        CompensationTransaction transaction,
        IEnumerable<PlanAssignment> payeeAssignments,
        DateOnly txDate,
        string txCurrency,
        IReadOnlyDictionary<Guid, string> planCurrencyById,
        IReadOnlySet<Guid> archivedPlanIds)
    {
        if (transaction.SelectedPlanAssignmentId is { } selectedId)
        {
            var resolution = PlanAssignmentResolver.ResolveSelected(
                payeeAssignments, txDate, txCurrency, planCurrencyById, archivedPlanIds, selectedId);

            if (!resolution.IsAccepted)
                throw new DomainException(resolution.RejectionReason!);

            return [resolution.Assignment!];
        }

        // Candidates already applies the three eligibility filters (Active + period covers the date +
        // plan currency matches). Its full list is now used instead of being narrowed to one.
        return PlanAssignmentResolver
            .Candidates(payeeAssignments, txDate, txCurrency, planCurrencyById, archivedPlanIds)
            .OrderBy(a => a.EffectivePeriod!.End.DayNumber - a.EffectivePeriod.Start.DayNumber)
            .ThenBy(a => a.Id)
            .ToList();
    }

    /// <summary>
    /// Runs every resolved assignment through the credit builder and accumulates the results.
    ///
    /// The covered-key set carries forward BETWEEN assignments, not just from the batch pre-load. That
    /// matters because a payee can hold two active assignments to the SAME plan (3 payees do today):
    /// both are eligible, both would evaluate the same rules, and without carrying the keys forward the
    /// second would try to insert a duplicate (transaction, plan, rule) and be stopped by the unique
    /// index — using the database as flow control, and double-counting that plan's attainment.
    /// </summary>
    private async Task<IReadOnlyList<Credit>> BuildCreditsForAllAsync(
        CompensationTransaction transaction,
        IReadOnlyList<PlanAssignment> assignments,
        Func<Guid, CompensationPlan?> planLookup,
        IReadOnlySet<(Guid TransactionId, Guid PlanId, Guid RuleId)> alreadyCredited,
        CancellationToken ct)
    {
        if (assignments.Count == 0) return Array.Empty<Credit>();

        var covered = new HashSet<(Guid, Guid, Guid)>(alreadyCredited);
        var allCredits = new List<Credit>();

        foreach (var assignment in assignments)
        {
            var plan = planLookup(assignment.PlanId);
            if (plan is null) continue;

            var credits = await BuildCreditsAsync(transaction, assignment, plan, covered, ct);

            foreach (var credit in credits)
                covered.Add((transaction.Id, credit.PlanId, credit.RuleId));

            allCredits.AddRange(credits);
        }

        return allCredits;
    }

    // ── Shared credit-building logic ──────────────────────────────────────────

    /// <param name="alreadyCredited">
    /// (TransactionId, PlanId, RuleId) triples that ALREADY have a live credit. Rules matching one of
    /// these are skipped instead of allocating a duplicate.
    ///
    /// This is the fine-grained half of anti-double-pay, and it lives here because this is the first
    /// point in the pipeline where plan and rule are known — the batch guard runs at candidate
    /// selection, long before <see cref="ResolveAssignment"/> decides anything. The set is supplied by
    /// the caller rather than queried here so the batch path keeps its "no DB queries per transaction"
    /// contract; querying inside this method would be an N+1 across the chunk.
    ///
    /// The unique index UX_Credits_Tenant_Transaction_Plan_Rule_Live is the last line of defence, not
    /// the control-flow mechanism: reaching it means this check was bypassed.
    /// </param>
    private async Task<IReadOnlyList<Credit>> BuildCreditsAsync(
        CompensationTransaction transaction,
        PlanAssignment assignment,
        CompensationPlan plan,
        IReadOnlySet<(Guid TransactionId, Guid PlanId, Guid RuleId)> alreadyCredited,
        CancellationToken ct)
    {
        // Multi-tenant guard.
        if (plan.TenantId != transaction.TenantId ||
            assignment.TenantId != transaction.TenantId)
            throw new InvalidOperationException(
                $"Tenant mismatch in credit allocation for transaction {transaction.Id}.");

        // Currency guard per Decision #45 edge cases.
        if (!string.Equals(plan.Currency, transaction.Amount.Currency,
            StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                $"Currency mismatch: transaction uses '{transaction.Amount.Currency}' " +
                $"but plan '{plan.Name}' is denominated in '{plan.Currency}'.");

        var now = _clock.UtcNowOffset;
        var txDate = transaction.TransactionDate;
        var credits = new List<Credit>();

        // Short-circuit: only load attainment data when at least one active rule needs it.
        // This avoids DB round-trips for Flat/Tiered plans (the common case).
        var attainmentPct = 1.0m;     // bracket-lookup path (SplitAtQuota = false)
        AttainmentSplitContext? splitContext = null; // split-at-quota path (SplitAtQuota = true)

        if (CommissionCalculator.PlanUsesAttainment(plan))
        {
            var needsBracket = plan.Rules.Any(r =>
                r.IsActive && r.RateTable.Type == RateTableType.AttainmentBased && !r.RateTable.SplitAtQuota);
            var needsSplit = plan.Rules.Any(r =>
                r.IsActive && r.RateTable.Type == RateTableType.AttainmentBased && r.RateTable.SplitAtQuota);

            if (needsBracket)
            {
                var attainment = await _quotaAttainmentService.ComputeAsync(
                    transaction.PayeeId!.Value, plan.Id, txDate, ct);
                attainmentPct = attainment.Value;
            }

            if (needsSplit)
            {
                splitContext = await _quotaAttainmentService.GetSplitContextAsync(
                    transaction.PayeeId!.Value, plan.Id, txDate, ct);
            }
        }

        // Decision #41: filter rules by EffectivePeriod at runtime.
        var applicableRules = plan.Rules
            .Where(r => r.IsActive &&
                (r.EffectivePeriod is null ||
                 (r.EffectivePeriod.Start <= txDate &&
                  r.EffectivePeriod.End >= txDate)))
            .ToList();

        foreach (var rule in applicableRules)
        {
            // Already credited for this exact (transaction, plan, rule) → nothing to add. Re-running
            // the job over an allocated transaction is therefore a no-op instead of a duplicate.
            if (alreadyCredited.Contains((transaction.Id, plan.Id, rule.Id)))
                continue;

            if (!CommissionCalculator.EvaluateTrigger(rule.Trigger, transaction, _logger))
                continue;

            // Defensive copy: avoid sharing the same Money instance with the tracked
            // transaction entity, which can confuse EF Core's owned-entity change tracker
            // and produce a NULL OriginalAmount in the INSERT.
            var baseAmount = Money.Of(transaction.Amount.Amount, transaction.Amount.Currency);

            Money commissionAmount;
            if (rule.Measurement.Type == MeasurementType.Units)
            {
                // Units: FlatRate is €/unit applied to transaction.Quantity.
                // Domain validation rejects Units + non-Flat at save time; this guard is a runtime safety net.
                if (rule.RateTable.Type != RateTableType.Flat)
                {
                    _logger.LogError(
                        "Rule {RuleId}: Units measurement requires Flat rate table (got {RateType}). " +
                        "Commission set to zero — data integrity issue, check plan configuration.",
                        rule.Id, rule.RateTable.Type);
                    commissionAmount = Money.Zero(plan.Currency);
                }
                else
                {
                    commissionAmount = CommissionCalculator.ComputeUnitsCommission(
                        transaction.Quantity, rule.RateTable.FlatRate!.Value, plan.Currency);
                }
            }
            else
            {
                // Revenue (default) and future measurement types: use transaction.Amount as base.
                if (rule.RateTable.Type == RateTableType.AttainmentBased && rule.RateTable.SplitAtQuota)
                {
                    if (splitContext is null)
                    {
                        // Phase 5 guard: no quota configured for this rep → zero commission.
                        _logger.LogWarning(
                            "Split-at-quota: no active quota for payee={PayeeId}, plan={PlanId}, date={Date}. " +
                            "Commission set to zero. Configure a quota to earn commission under this rule.",
                            transaction.PayeeId, plan.Id, txDate);
                        commissionAmount = Money.Zero(baseAmount.Currency);
                    }
                    else
                    {
                        commissionAmount = CommissionCalculator.ComputeAttainmentSplitCommission(
                            baseAmount, rule.RateTable.AttainmentTiers!,
                            splitContext.PriorCumulative, splitContext.QuotaTarget);
                    }
                }
                else
                {
                    commissionAmount = CommissionCalculator.ComputeCommission(baseAmount, rule.RateTable, attainmentPct);
                }
            }

            commissionAmount = CommissionCalculator.ApplyModifier(commissionAmount, baseAmount, rule.Modifier);
            commissionAmount = CommissionCalculator.ApplyCap(commissionAmount, rule.Cap);
            commissionAmount = CommissionCalculator.ApplyFloor(commissionAmount, rule.Floor);

            // Defensive copy (same reasoning as baseAmount above): ApplyCap/ApplyFloor may return the
            // rule's OWN cap/floor Money instance, which is shared across every capped transaction in
            // the chunk (the rule is loaded once per chunk). Each Credit must own an EXCLUSIVE Money
            // instance, or EF's owned-type tracker throws "CreditedAmount#Money.CreditId is part of a
            // key and cannot be modified" on the second Credit. Same Amount/Currency → payout unchanged.
            var creditedAmount = Money.Of(commissionAmount.Amount, commissionAmount.Currency);

            var snapshot = RuleSnapshot.Freeze(
                rule.Id, plan.Id, plan.Version, rule.Name,
                rule.RateTable, rule.Trigger, now, measurement: rule.Measurement);

            var credit = Credit.Allocate(
                tenantId: transaction.TenantId,
                transactionId: transaction.Id,
                payeeId: transaction.PayeeId!.Value,
                planId: plan.Id,
                ruleId: rule.Id,
                ruleSnapshot: snapshot,
                originalAmount: baseAmount,
                creditedAmount: creditedAmount,
                splitPercentage: Percentage.FromPercent(100),
                role: CreditRole.Primary,
                allocatedBy: transaction.IngestedBy,
                id: _guidGenerator.NewGuid(),
                now: now,
                eventId: _guidGenerator.NewGuid());

            credits.Add(credit);
        }

        return credits;
    }

}
