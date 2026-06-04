using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
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

    public CreditAllocationService(
        IApplicationDbContext db,
        IGuidGenerator guidGenerator,
        IClock clock,
        ILogger<CreditAllocationService> logger)
    {
        _db = db;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _logger = logger;
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

        return BuildCredits(transaction, assignment, plan);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Credit>> AllocateAsync(
        CompensationTransaction transaction,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlanAssignment>> assignmentsByPayee,
        IReadOnlyDictionary<Guid, CompensationPlan> plansById,
        CancellationToken ct = default)
    {
        // Decision #44: unassigned transactions never produce Credits.
        if (transaction.PayeeId == null)
            return Task.FromResult<IReadOnlyList<Credit>>(Array.Empty<Credit>());

        // Only Pending transactions get processed.
        if (transaction.Status != CompensationTransactionStatus.Pending)
            return Task.FromResult<IReadOnlyList<Credit>>(Array.Empty<Credit>());

        var payeeIdVal = transaction.PayeeId.Value;
        var txDate = transaction.TransactionDate;

        // Resolve assignment from the caller-supplied pre-loaded dictionary (no DB query).
        if (!assignmentsByPayee.TryGetValue(payeeIdVal, out var payeeAssignments))
            return Task.FromResult<IReadOnlyList<Credit>>(Array.Empty<Credit>());

        // Pattern B: build planCurrencyById from the pre-loaded plans dictionary (no DB query).
        var planCurrencyById = plansById.ToDictionary(kv => kv.Key, kv => kv.Value.Currency);

        // Pattern B resolution: pick the assignment whose plan currency matches the transaction currency.
        var assignment = PlanAssignmentResolver.Resolve(
            payeeAssignments, txDate, transaction.Amount.Currency, planCurrencyById);

        if (assignment is null)
            return Task.FromResult<IReadOnlyList<Credit>>(Array.Empty<Credit>());

        // Resolve plan from the caller-supplied pre-loaded dictionary (no DB query).
        if (!plansById.TryGetValue(assignment.PlanId, out var plan))
            return Task.FromResult<IReadOnlyList<Credit>>(Array.Empty<Credit>());

        return Task.FromResult(BuildCredits(transaction, assignment, plan));
    }

    // ── Shared credit-building logic ──────────────────────────────────────────

    private IReadOnlyList<Credit> BuildCredits(
        CompensationTransaction transaction,
        PlanAssignment assignment,
        CompensationPlan plan)
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

        // Decision #41: filter rules by EffectivePeriod at runtime.
        var applicableRules = plan.Rules
            .Where(r => r.IsActive &&
                (r.EffectivePeriod is null ||
                 (r.EffectivePeriod.Start <= txDate &&
                  r.EffectivePeriod.End >= txDate)))
            .ToList();

        foreach (var rule in applicableRules)
        {
            if (!EvaluateTrigger(rule.Trigger, transaction))
                continue;

            var baseAmount = transaction.Amount;
            var commissionAmount = ComputeCommission(baseAmount, rule.RateTable);
            commissionAmount = ApplyModifier(commissionAmount, baseAmount, rule.Modifier);
            commissionAmount = ApplyCap(commissionAmount, rule.Cap);
            commissionAmount = ApplyFloor(commissionAmount, rule.Floor);

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

    // ── Trigger evaluation ────────────────────────────────────────────────────

    private bool EvaluateTrigger(Trigger trigger, CompensationTransaction tx)
    {
        if (trigger.Conditions.Count == 0) return true; // Trigger.Always

        var results = trigger.Conditions.Select(c => EvaluateCondition(c, tx));

        return trigger.LogicalOperator == LogicalOperator.And
            ? results.All(r => r)
            : results.Any(r => r);
    }

    private bool EvaluateCondition(Condition condition, CompensationTransaction tx)
    {
        string? fieldValue = condition.Field.ToLowerInvariant() switch
        {
            "transactionamount" => tx.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            "transactiondate" => tx.TransactionDate.ToString("yyyy-MM-dd"),
            "source" => tx.Source.ToString(),
            _ => null
        };

        if (fieldValue == null)
        {
            _logger.LogWarning(
                "CreditEngine: unknown condition field '{Field}' on rule — treating as not-matched.",
                condition.Field);
            return false;
        }

        return condition.Value.Type switch
        {
            ConditionValueType.Number => EvaluateNumeric(fieldValue, condition.Operator, condition.Value.Raw),
            ConditionValueType.Date => EvaluateDate(fieldValue, condition.Operator, condition.Value.Raw),
            ConditionValueType.String => EvaluateString(fieldValue, condition.Operator, condition.Value.Raw, condition.Value.Set),
            ConditionValueType.Boolean => EvaluateBoolean(fieldValue, condition.Operator, condition.Value.Raw),
            _ => false
        };
    }

    private static bool EvaluateNumeric(string fieldValue, ConditionOperator op, string raw)
    {
        if (!decimal.TryParse(fieldValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var field))
            return false;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold))
            return false;

        return op switch
        {
            ConditionOperator.Equal => field == threshold,
            ConditionOperator.NotEqual => field != threshold,
            ConditionOperator.GreaterThan => field > threshold,
            ConditionOperator.GreaterThanOrEqual => field >= threshold,
            ConditionOperator.LessThan => field < threshold,
            ConditionOperator.LessThanOrEqual => field <= threshold,
            _ => false
        };
    }

    private static bool EvaluateDate(string fieldValue, ConditionOperator op, string raw)
    {
        if (!DateOnly.TryParseExact(fieldValue, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var field))
            return false;
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var threshold))
            return false;

        return op switch
        {
            ConditionOperator.Equal => field == threshold,
            ConditionOperator.NotEqual => field != threshold,
            ConditionOperator.GreaterThan => field > threshold,
            ConditionOperator.GreaterThanOrEqual => field >= threshold,
            ConditionOperator.LessThan => field < threshold,
            ConditionOperator.LessThanOrEqual => field <= threshold,
            _ => false
        };
    }

    private static bool EvaluateString(string fieldValue, ConditionOperator op, string raw, IReadOnlyList<string>? set)
    {
        return op switch
        {
            ConditionOperator.Equal => string.Equals(fieldValue, raw, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEqual => !string.Equals(fieldValue, raw, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.In => set != null && set.Any(s => string.Equals(fieldValue, s, StringComparison.OrdinalIgnoreCase)),
            ConditionOperator.NotIn => set == null || !set.Any(s => string.Equals(fieldValue, s, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static bool EvaluateBoolean(string fieldValue, ConditionOperator op, string raw)
    {
        if (!bool.TryParse(fieldValue, out var field)) return false;
        if (!bool.TryParse(raw, out var threshold)) return false;

        return op switch
        {
            ConditionOperator.Equal => field == threshold,
            ConditionOperator.NotEqual => field != threshold,
            _ => false
        };
    }

    // ── Commission computation ────────────────────────────────────────────────

    private static Money ComputeCommission(Money baseAmount, RateTable rateTable)
    {
        return rateTable.Type switch
        {
            RateTableType.Flat => baseAmount.Multiply(rateTable.FlatRate!.Value),
            RateTableType.Tiered => ComputeTieredCommission(baseAmount, rateTable.Tiers!),
            RateTableType.AttainmentBased =>
                // TODO WI-CALC-A.2: replace with real attainment from IQuotaAttainmentService.
                // V1 stub: treat attainment as 100% (picks bracket containing 1.0).
                ComputeAttainmentCommission(baseAmount, rateTable.AttainmentTiers!, attainmentPct: 1.0m),
            _ => throw new DomainException($"Unsupported RateTableType: {rateTable.Type}")
        };
    }

    private static Money ComputeTieredCommission(Money baseAmount, IReadOnlyList<RateTier> tiers)
    {
        // Walk tiers: each tier applies its Rate to the portion of baseAmount within [From, To).
        // If the last tier has To == null, it applies to everything above From.
        var total = Money.Zero(baseAmount.Currency);
        var remaining = baseAmount.Amount;

        foreach (var tier in tiers)
        {
            if (remaining <= 0) break;

            var tierMin = tier.From;
            var tierMax = tier.To;

            // How much of the base falls in this tier.
            var inTier = tierMax.HasValue
                ? Math.Min(remaining, tierMax.Value - tierMin)
                : remaining;

            if (inTier <= 0) continue;

            total = total.Add(Money.Of(inTier * tier.Rate, baseAmount.Currency));
            remaining -= inTier;
        }

        return total;
    }

    private static Money ComputeAttainmentCommission(Money baseAmount, IReadOnlyList<AttainmentTier> tiers, decimal attainmentPct)
    {
        // Find the bracket that contains attainmentPct.
        var tier = tiers.LastOrDefault(t =>
            t.AttainmentFrom <= attainmentPct &&
            (t.AttainmentTo == null || attainmentPct <= t.AttainmentTo.Value));

        if (tier == null) return Money.Zero(baseAmount.Currency);
        return baseAmount.Multiply(tier.Rate);
    }

    // ── Modifiers, caps, floors ───────────────────────────────────────────────

    private static Money ApplyModifier(Money commission, Money baseAmount, Modifier? modifier)
    {
        if (modifier == null) return commission;
        // Multiplier and Accelerator: multiply commission by factor.
        // Spiff: V1 stub — treat Factor as multiplier.
        return commission.Multiply(modifier.Factor);
    }

    private static Money ApplyCap(Money commission, Cap? cap)
    {
        if (cap == null) return commission;
        if (cap.Scope == CapScope.PerTransaction)
        {
            var capAmount = cap.Amount;
            if (!string.Equals(commission.Currency, capAmount.Currency, StringComparison.OrdinalIgnoreCase))
                return commission; // Currency mismatch on cap — skip cap.
            return commission > capAmount ? capAmount : commission;
        }
        // PerPeriod and Total caps: deferred to WI-CALC-A.4 (Payout Engine has period context).
        return commission;
    }

    private static Money ApplyFloor(Money commission, Floor? floor)
    {
        if (floor == null) return commission;
        var floorAmount = floor.Amount;
        if (!string.Equals(commission.Currency, floorAmount.Currency, StringComparison.OrdinalIgnoreCase))
            return commission; // Currency mismatch on floor — skip floor.
        return commission < floorAmount ? floorAmount : commission;
    }
}
