using System.Globalization;
using Microsoft.Extensions.Logging;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.Infrastructure.Compensation.Calculation;

/// <summary>
/// Pure static computation core extracted from CreditAllocationService.
/// All methods are internal so Wasnie.UnitTests can exercise them directly
/// without needing a database or DI container.
/// Logic is identical to the original private methods — only visibility changed.
/// </summary>
internal static class CommissionCalculator
{
    // ── Plan inspection ───────────────────────────────────────────────────────

    internal static bool PlanUsesAttainment(CompensationPlan plan) =>
        plan.Rules.Any(r => r.IsActive && r.RateTable.Type == RateTableType.AttainmentBased);

    // ── Trigger evaluation ────────────────────────────────────────────────────

    internal static bool EvaluateTrigger(
        Trigger trigger,
        CompensationTransaction tx,
        ILogger? logger = null)
    {
        if (trigger.Conditions.Count == 0) return true; // Trigger.Always

        var results = trigger.Conditions.Select(c => EvaluateCondition(c, tx, logger));

        return trigger.LogicalOperator == LogicalOperator.And
            ? results.All(r => r)
            : results.Any(r => r);
    }

    internal static bool EvaluateCondition(
        Condition condition,
        CompensationTransaction tx,
        ILogger? logger = null)
    {
        string? fieldValue = condition.Field.ToLowerInvariant() switch
        {
            "transactionamount" => tx.Amount.Amount.ToString(CultureInfo.InvariantCulture),
            "transactiondate"   => tx.TransactionDate.ToString("yyyy-MM-dd"),
            "source"            => tx.Source.ToString(),
            _                   => null
        };

        if (fieldValue == null)
        {
            logger?.LogWarning(
                "CreditEngine: unknown condition field '{Field}' on rule — treating as not-matched.",
                condition.Field);
            return false;
        }

        return condition.Value.Type switch
        {
            ConditionValueType.Number  => EvaluateNumeric(fieldValue, condition.Operator, condition.Value.Raw),
            ConditionValueType.Date    => EvaluateDate(fieldValue, condition.Operator, condition.Value.Raw),
            ConditionValueType.String  => EvaluateString(fieldValue, condition.Operator, condition.Value.Raw, condition.Value.Set),
            ConditionValueType.Boolean => EvaluateBoolean(fieldValue, condition.Operator, condition.Value.Raw),
            _                          => false
        };
    }

    internal static bool EvaluateNumeric(string fieldValue, ConditionOperator op, string raw)
    {
        if (!decimal.TryParse(fieldValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var field))
            return false;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold))
            return false;

        return op switch
        {
            ConditionOperator.Equal              => field == threshold,
            ConditionOperator.NotEqual           => field != threshold,
            ConditionOperator.GreaterThan        => field > threshold,
            ConditionOperator.GreaterThanOrEqual => field >= threshold,
            ConditionOperator.LessThan           => field < threshold,
            ConditionOperator.LessThanOrEqual    => field <= threshold,
            _                                    => false
        };
    }

    internal static bool EvaluateDate(string fieldValue, ConditionOperator op, string raw)
    {
        if (!DateOnly.TryParseExact(fieldValue, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var field))
            return false;
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var threshold))
            return false;

        return op switch
        {
            ConditionOperator.Equal              => field == threshold,
            ConditionOperator.NotEqual           => field != threshold,
            ConditionOperator.GreaterThan        => field > threshold,
            ConditionOperator.GreaterThanOrEqual => field >= threshold,
            ConditionOperator.LessThan           => field < threshold,
            ConditionOperator.LessThanOrEqual    => field <= threshold,
            _                                    => false
        };
    }

    internal static bool EvaluateString(
        string fieldValue,
        ConditionOperator op,
        string raw,
        IReadOnlyList<string>? set)
    {
        return op switch
        {
            ConditionOperator.Equal    => string.Equals(fieldValue, raw, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEqual => !string.Equals(fieldValue, raw, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.In       => set != null && set.Any(s => string.Equals(fieldValue, s, StringComparison.OrdinalIgnoreCase)),
            ConditionOperator.NotIn    => set == null || !set.Any(s => string.Equals(fieldValue, s, StringComparison.OrdinalIgnoreCase)),
            _                          => false
        };
    }

    internal static bool EvaluateBoolean(string fieldValue, ConditionOperator op, string raw)
    {
        if (!bool.TryParse(fieldValue, out var field)) return false;
        if (!bool.TryParse(raw, out var threshold)) return false;

        return op switch
        {
            ConditionOperator.Equal    => field == threshold,
            ConditionOperator.NotEqual => field != threshold,
            _                          => false
        };
    }

    // ── Commission computation ────────────────────────────────────────────────

    internal static Money ComputeCommission(Money baseAmount, RateTable rateTable, decimal attainmentPct)
    {
        return rateTable.Type switch
        {
            RateTableType.Flat            => baseAmount.Multiply(rateTable.FlatRate!.Value),
            RateTableType.Tiered          => ComputeTieredCommission(baseAmount, rateTable.Tiers!),
            RateTableType.AttainmentBased => ComputeAttainmentCommission(baseAmount, rateTable.AttainmentTiers!, attainmentPct),
            _                             => throw new DomainException($"Unsupported RateTableType: {rateTable.Type}")
        };
    }

    internal static Money ComputeTieredCommission(Money baseAmount, IReadOnlyList<RateTier> tiers)
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

            var inTier = tierMax.HasValue
                ? Math.Min(remaining, tierMax.Value - tierMin)
                : remaining;

            if (inTier <= 0) continue;

            total = total.Add(Money.Of(inTier * tier.Rate, baseAmount.Currency));
            remaining -= inTier;
        }

        return total;
    }

    internal static Money ComputeAttainmentCommission(
        Money baseAmount,
        IReadOnlyList<AttainmentTier> tiers,
        decimal attainmentPct)
    {
        // Find the bracket that contains attainmentPct.
        // LastOrDefault handles overlapping lower/upper boundary at adjacent tier edges
        // (e.g. attainment=1.00 matches [0.50,1.00] AND [1.00,null] — last wins).
        var tier = tiers.LastOrDefault(t =>
            t.AttainmentFrom <= attainmentPct &&
            (t.AttainmentTo == null || attainmentPct <= t.AttainmentTo.Value));

        if (tier == null) return Money.Zero(baseAmount.Currency);
        return baseAmount.Multiply(tier.Rate);
    }

    /// <summary>
    /// Split-at-quota attainment: walks every tier and earns each tier's rate on the
    /// portion of this transaction that falls within that tier's absolute revenue range
    /// [AttainmentFrom * quota, AttainmentTo * quota). The transaction's revenue interval
    /// is [priorCumulative, priorCumulative + txAmount]. Each tier contributes its rate
    /// to the overlap between the transaction interval and the tier's absolute range.
    /// </summary>
    internal static Money ComputeAttainmentSplitCommission(
        Money txAmount,
        IReadOnlyList<AttainmentTier> tiers,
        decimal priorCumulative,
        decimal quotaTarget)
    {
        if (quotaTarget <= 0m) return Money.Zero(txAmount.Currency);

        var txValue = txAmount.Amount;
        var txStart = priorCumulative;
        var txEnd = priorCumulative + txValue;
        var total = 0m;

        foreach (var tier in tiers)
        {
            var tierFloor = tier.AttainmentFrom * quotaTarget;
            var tierCeiling = tier.AttainmentTo.HasValue
                ? tier.AttainmentTo.Value * quotaTarget
                : decimal.MaxValue;

            var overlapStart = Math.Max(txStart, tierFloor);
            var overlapEnd = Math.Min(txEnd, tierCeiling);

            if (overlapEnd > overlapStart)
                total += (overlapEnd - overlapStart) * tier.Rate;
        }

        return Money.Of(total, txAmount.Currency);
    }

    // ── Modifiers, caps, floors ───────────────────────────────────────────────

    internal static Money ApplyModifier(Money commission, Money baseAmount, Modifier? modifier)
    {
        if (modifier == null) return commission;
        // Multiplier and Accelerator: multiply commission by factor.
        // Spiff: V1 stub — treat Factor as multiplier.
        return commission.Multiply(modifier.Factor);
    }

    internal static Money ApplyCap(Money commission, Cap? cap)
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

    internal static Money ApplyFloor(Money commission, Floor? floor)
    {
        if (floor == null) return commission;
        var floorAmount = floor.Amount;
        if (!string.Equals(commission.Currency, floorAmount.Currency, StringComparison.OrdinalIgnoreCase))
            return commission; // Currency mismatch on floor — skip floor.
        return commission < floorAmount ? floorAmount : commission;
    }
}
