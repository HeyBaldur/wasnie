using System.Globalization;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Plans;
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
        // Resolution comes from TriggerFieldCatalog, the same list the field picker and the save-time
        // validator use. It used to be a private switch here, which is how the UI ended up offering
        // names the engine had never heard of.
        var definition = TriggerFieldCatalog.Find(condition.Field);

        if (definition is null)
        {
            logger?.LogWarning(
                "CreditEngine: condition field '{Field}' is not a known transaction attribute — rule " +
                "cannot match. Edit the rule and pick a field from the list.",
                condition.Field);
            return false;
        }

        var fieldValue = definition.Resolve(tx);

        if (fieldValue is null)
        {
            // A KNOWN field the transaction simply has no value for (e.g. a SKU on a deal-level row
            // with no line item). Not a misconfiguration — the condition just does not match.
            logger?.LogDebug(
                "CreditEngine: transaction {TxId} has no value for '{Field}' — condition not matched.",
                tx.Id, definition.Field);
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

    /// <summary>
    /// Units mode: flatRatePerUnit is the monetary amount earned per unit (e.g. €2.00).
    /// commission = ratePerUnit × quantity.
    /// Only valid with Flat rate tables — callers must validate before calling.
    /// </summary>
    internal static Money ComputeUnitsCommission(int quantity, decimal ratePerUnit, string currency)
        => Money.Of(ratePerUnit * quantity, currency);

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

    internal static Money ComputeTieredCommission(
        Money baseAmount,
        IReadOnlyList<RateTier> tiers,
        List<RateTierStep>? tierTrace = null)
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

            var tierAmount = Money.Of(inTier * tier.Rate, baseAmount.Currency);
            // Null when nobody asked: `?.Add(expr)` does not evaluate expr, so an unobserved pay run
            // allocates nothing here.
            tierTrace?.Add(new RateTierStep(tier.From, tier.To, tier.Rate, inTier, tierAmount));

            total = total.Add(tierAmount);
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
        decimal quotaTarget,
        List<RateTierStep>? tierTrace = null)
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
            {
                var portion = overlapEnd - overlapStart;
                total += portion * tier.Rate;
                tierTrace?.Add(new RateTierStep(
                    tier.AttainmentFrom, tier.AttainmentTo, tier.Rate,
                    portion, Money.Of(portion * tier.Rate, txAmount.Currency)));
            }
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

    // ── The cascade, and the only place it lives ──────────────────────────────

    /// <summary>
    /// The outcome of running one rule over one transaction.
    ///
    /// ★ <c>CreditGenerated</c> IS CARRIED, NOT INFERRED FROM THE AMOUNT. A trigger that does not
    /// match produces no credit; a rule that matched and computed nothing produces a credit of zero.
    /// Collapsing those into "the amount is zero" is how a breakdown ends up telling somebody their
    /// rule paid nothing when in fact it never applied to them.
    /// </summary>
    internal readonly record struct RuleEvaluation(bool CreditGenerated, Money BaseAmount, Money Commission);

    /// <summary>
    /// Runs a rule over a transaction and, optionally, reports how it got there.
    ///
    /// ★★ THIS IS THE SEQUENCE THAT USED TO LIVE INLINE IN CreditAllocationService, MOVED HERE
    /// UNCHANGED. It is one method rather than two so that anything explaining a payout and the pay
    /// run that produced it can never disagree: a second implementation "just for previews" is two
    /// financial engines that drift, and the one people look at would be the one that is wrong.
    ///
    /// ★ THE ORDER IS THE ENGINE'S, NOT A LOGICAL ONE. Rate, then modifier, then cap, then FLOOR —
    /// so a floor set above a cap wins and the rule pays more than its own ceiling. Anybody
    /// reconstructing this cascade from the rule's fields would put the floor before the cap and
    /// arrive at a different number.
    ///
    /// ★ <paramref name="trace"/> IS OPTIONAL AND COSTS NOTHING WHEN NULL. Every emission goes
    /// through <c>trace?.Add(...)</c>, and C# does not evaluate the argument of a null-conditional
    /// call — so a pay run over a million transactions allocates no steps nobody will read.
    /// </summary>
    /// <param name="attainmentSource">
    /// ★ Where <paramref name="attainmentPct"/> came from, so the trace can say so. The engine's own
    /// default for that value is 1.0 — a rep at full quota — which is a number that looks entirely
    /// reasonable and is false for almost everybody. A breakdown that presents a defaulted figure as
    /// a measured one is worse than one that refuses to answer, which is why this is three states
    /// and not a boolean.
    /// </param>
    internal static RuleEvaluation Evaluate(
        Rule rule,
        CompensationTransaction tx,
        string planCurrency,
        decimal attainmentPct,
        AttainmentSplitContext? splitContext,
        ILogger? logger = null,
        List<RuleCalculationStep>? trace = null,
        AttainmentSource attainmentSource = AttainmentSource.Measured)
    {
        // ── 1. Trigger ───────────────────────────────────────────────────────
        if (!EvaluateTrigger(rule.Trigger, tx, logger))
        {
            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Trigger,
                Outcome = RuleCalculationOutcome.NotMatched,
            });

            return new RuleEvaluation(false, Money.Zero(planCurrency), Money.Zero(planCurrency));
        }

        trace?.Add(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Trigger,
            Outcome = RuleCalculationOutcome.Applied,
        });

        // ── 2. Base ──────────────────────────────────────────────────────────
        // Defensive copy: avoid sharing the same Money instance with the tracked transaction entity,
        // which can confuse EF Core's owned-entity change tracker and produce a NULL OriginalAmount
        // in the INSERT.
        var baseAmount = Money.Of(tx.Amount.Amount, tx.Amount.Currency);

        trace?.Add(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Base,
            Outcome = RuleCalculationOutcome.Applied,
            Output = baseAmount,
        });

        // ── 3. Rate ──────────────────────────────────────────────────────────
        var commission = ComputeRate(
            rule, tx, baseAmount, planCurrency, attainmentPct, splitContext,
            logger, trace, attainmentSource);

        // ── 4/5/6. Modifier, then cap, then floor ────────────────────────────
        commission = TraceModifier(commission, baseAmount, rule.Modifier, trace);
        commission = TraceCap(commission, rule.Cap, trace);
        commission = TraceFloor(commission, rule.Floor, trace);

        return new RuleEvaluation(true, baseAmount, commission);
    }

    private static Money ComputeRate(
        Rule rule,
        CompensationTransaction tx,
        Money baseAmount,
        string planCurrency,
        decimal attainmentPct,
        AttainmentSplitContext? splitContext,
        ILogger? logger,
        List<RuleCalculationStep>? trace,
        AttainmentSource attainmentSource)
    {
        if (rule.Measurement.Type == MeasurementType.Units)
        {
            // Units: FlatRate is per-unit money applied to transaction.Quantity.
            // Domain validation rejects Units + non-Flat at save time; this guard is a runtime safety net.
            if (rule.RateTable.Type != RateTableType.Flat)
            {
                logger?.LogError(
                    "Rule {RuleId}: Units measurement requires Flat rate table (got {RateType}). " +
                    "Commission set to zero — data integrity issue, check plan configuration.",
                    rule.Id, rule.RateTable.Type);

                var zero = Money.Zero(planCurrency);
                trace?.Add(new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Rate,
                    Outcome = RuleCalculationOutcome.Skipped,
                    Input = baseAmount,
                    Output = zero,
                    RateTable = rule.RateTable.Type,
                });
                return zero;
            }

            var units = ComputeUnitsCommission(tx.Quantity, rule.RateTable.FlatRate!.Value, planCurrency);
            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Applied,
                Input = baseAmount,
                Output = units,
                // For Units the operand people care about is the quantity; the per-unit money sits
                // in Threshold because it is the rule's own figure rather than the transaction's.
                Operand = tx.Quantity,
                Threshold = Money.Of(rule.RateTable.FlatRate!.Value, planCurrency),
                RateTable = rule.RateTable.Type,
            });
            return units;
        }

        // Revenue (default) and future measurement types: use transaction.Amount as base.
        if (rule.RateTable.Type == RateTableType.AttainmentBased && rule.RateTable.SplitAtQuota)
        {
            if (splitContext is null)
            {
                // Phase 5 guard: no quota configured for this rep → zero commission.
                logger?.LogWarning(
                    "Split-at-quota: no active quota for payee={PayeeId}, plan={PlanId}, date={Date}. " +
                    "Commission set to zero. Configure a quota to earn commission under this rule.",
                    tx.PayeeId, rule.PlanId, tx.TransactionDate);

                var zero = Money.Zero(baseAmount.Currency);
                trace?.Add(new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Rate,
                    Outcome = RuleCalculationOutcome.Skipped,
                    Input = baseAmount,
                    Output = zero,
                    RateTable = rule.RateTable.Type,
                });
                return zero;
            }

            List<RateTierStep>? splitTiers = trace is null ? null : new List<RateTierStep>();
            var split = ComputeAttainmentSplitCommission(
                baseAmount, rule.RateTable.AttainmentTiers!,
                splitContext.PriorCumulative, splitContext.QuotaTarget, splitTiers);

            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Applied,
                Input = baseAmount,
                Output = split,
                RateTable = rule.RateTable.Type,
                Tiers = splitTiers,
            });
            return split;
        }

        if (rule.RateTable.Type == RateTableType.Tiered)
        {
            List<RateTierStep>? walked = trace is null ? null : new List<RateTierStep>();
            var tiered = ComputeTieredCommission(baseAmount, rule.RateTable.Tiers!, walked);

            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Applied,
                Input = baseAmount,
                Output = tiered,
                RateTable = rule.RateTable.Type,
                Tiers = walked,
            });
            return tiered;
        }

        var result = ComputeCommission(baseAmount, rule.RateTable, attainmentPct);
        trace?.Add(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Rate,
            Outcome = RuleCalculationOutcome.Applied,
            Input = baseAmount,
            Output = result,
            Operand = rule.RateTable.Type == RateTableType.Flat
                ? rule.RateTable.FlatRate
                : attainmentPct,
            RateTable = rule.RateTable.Type,
            AttainmentSource = rule.RateTable.Type == RateTableType.AttainmentBased
                ? attainmentSource
                : null,
        });
        return result;
    }

    // ── The traced wrappers ──────────────────────────────────────────────────
    //
    // ★ THE AMOUNT ALWAYS COMES FROM THE UNTRACED METHOD. These wrappers never re-derive money: they
    // call ApplyModifier/ApplyCap/ApplyFloor and then classify what happened by reading the
    // component's own fields — is there a cap at all, what currency is it in, what scope. Money
    // logic stays in exactly one place, which is the whole point of this work item.

    private static Money TraceModifier(
        Money commission, Money baseAmount, Modifier? modifier, List<RuleCalculationStep>? trace)
    {
        var result = ApplyModifier(commission, baseAmount, modifier);
        trace?.Add(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Modifier,
            Outcome = modifier is null
                ? RuleCalculationOutcome.NotConfigured
                : Classify(commission, result),
            Input = commission,
            Output = result,
            Operand = modifier?.Factor,
        });
        return result;
    }

    private static Money TraceCap(Money commission, Cap? cap, List<RuleCalculationStep>? trace)
    {
        var result = ApplyCap(commission, cap);
        trace?.Add(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Cap,
            Outcome = CapOutcome(commission, result, cap),
            Input = commission,
            Output = result,
            Threshold = cap?.Amount,
        });
        return result;
    }

    private static Money TraceFloor(Money commission, Floor? floor, List<RuleCalculationStep>? trace)
    {
        var result = ApplyFloor(commission, floor);
        trace?.Add(new RuleCalculationStep
        {
            Component = RuleCalculationComponent.Floor,
            Outcome = floor is null
                ? RuleCalculationOutcome.NotConfigured
                : SameCurrency(commission, floor.Amount)
                    ? Classify(commission, result)
                    : RuleCalculationOutcome.Skipped,
            Input = commission,
            Output = result,
            Threshold = floor?.Amount,
        });
        return result;
    }

    private static RuleCalculationOutcome CapOutcome(Money before, Money after, Cap? cap)
    {
        if (cap is null) return RuleCalculationOutcome.NotConfigured;

        // A scope this engine does not honour, or a cap in another currency, is not "a cap that did
        // not bite" — it is a cap that was never consulted, and whoever audits the payout has to be
        // able to tell those apart.
        if (cap.Scope != CapScope.PerTransaction) return RuleCalculationOutcome.Skipped;
        if (!SameCurrency(before, cap.Amount)) return RuleCalculationOutcome.Skipped;

        return Classify(before, after);
    }

    private static bool SameCurrency(Money a, Money b) =>
        string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase);

    private static RuleCalculationOutcome Classify(Money before, Money after) =>
        before.Amount == after.Amount
            ? RuleCalculationOutcome.AppliedWithoutEffect
            : RuleCalculationOutcome.Applied;
}
