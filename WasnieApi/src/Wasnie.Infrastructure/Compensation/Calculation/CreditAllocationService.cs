using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

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
        var planCurrencyById = assignmentPlanIds.Count > 0
            ? (await _db.CompensationPlans
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && assignmentPlanIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Currency })
                .ToListAsync(ct))
                .ToDictionary(p => p.Id, p => p.Currency)
            : new Dictionary<Guid, string>();

        // Pattern B resolution: pick the assignment whose plan currency matches the transaction currency.
        var assignment = PlanAssignmentResolver.Resolve(
            allPayeeAssignments, txDate, transaction.Amount.Currency, planCurrencyById);

        if (assignment is null) return Array.Empty<Credit>();

        // Load Plan with rules eagerly — Rules is a related entity collection, not auto-loaded.
        var planId = assignment.PlanId;
        var plan = await _db.CompensationPlans
            .IgnoreQueryFilters()
            .Include(p => p.Rules)
            .Where(p => p.Id == planId && p.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (plan is null) return Array.Empty<Credit>();

        return await BuildCreditsAsync(transaction, assignment, plan, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlanAssignment>> assignmentsByPayee,
        IReadOnlyDictionary<Guid, CompensationPlan> plansById,
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

        // Pattern B: build planCurrencyById from the pre-loaded plans dictionary (no DB query).
        var planCurrencyById = plansById.ToDictionary(kv => kv.Key, kv => kv.Value.Currency);

        // Pattern B resolution: pick the assignment whose plan currency matches the transaction currency.
        var assignment = PlanAssignmentResolver.Resolve(
            payeeAssignments, txDate, transaction.Amount.Currency, planCurrencyById);

        if (assignment is null)
            return Array.Empty<Credit>();

        // Resolve plan from the caller-supplied pre-loaded dictionary (no DB query).
        if (!plansById.TryGetValue(assignment.PlanId, out var plan))
            return Array.Empty<Credit>();

        return await BuildCreditsAsync(transaction, assignment, plan, ct);
    }

    // ── Shared credit-building logic ──────────────────────────────────────────

    private async Task<IReadOnlyList<Credit>> BuildCreditsAsync(
        CompensationTransaction transaction,
        PlanAssignment assignment,
        CompensationPlan plan,
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
            if (!CommissionCalculator.EvaluateTrigger(rule.Trigger, transaction, _logger))
                continue;

            var baseAmount = transaction.Amount;

            Money commissionAmount;
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
            commissionAmount = CommissionCalculator.ApplyModifier(commissionAmount, baseAmount, rule.Modifier);
            commissionAmount = CommissionCalculator.ApplyCap(commissionAmount, rule.Cap);
            commissionAmount = CommissionCalculator.ApplyFloor(commissionAmount, rule.Floor);

            var snapshot = RuleSnapshot.Freeze(
                rule.Id, plan.Id, plan.Version, rule.Name,
                rule.RateTable, rule.Trigger, now);

            var credit = Credit.Allocate(
                tenantId: transaction.TenantId,
                transactionId: transaction.Id,
                payeeId: transaction.PayeeId!.Value,
                planId: plan.Id,
                ruleId: rule.Id,
                ruleSnapshot: snapshot,
                originalAmount: baseAmount,
                creditedAmount: commissionAmount,
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
