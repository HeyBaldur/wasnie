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

    /// <summary>
    /// What a ladder walk produced AND how much of the transaction it never priced.
    ///
    /// ★★ THE UNPRICED REMAINDER IS AN OUTPUT OF THE WALK, NOT A SECOND CALCULATION. Working it out
    /// separately — "base minus the last tier's ceiling" — would be a second, subtly different model
    /// of the same ladder, and the two would agree until a ladder with gaps arrived and they did not.
    /// The loop already knows exactly what it could not place; this just stops throwing that away.
    /// </summary>
    internal readonly record struct LadderWalk(Money Commission, decimal Unpriced);

    /// <summary>
    /// ★ THE ORIGINAL SIGNATURE, UNCHANGED AND STILL THE ONE MOST CALLERS WANT. It delegates rather
    /// than duplicating the loop, so there is exactly one tiered walk in the engine.
    /// </summary>
    internal static Money ComputeTieredCommission(
        Money baseAmount,
        IReadOnlyList<RateTier> tiers,
        List<RateTierStep>? tierTrace = null)
        => WalkTiers(baseAmount, tiers, tierTrace).Commission;

    internal static LadderWalk WalkTiers(
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

        // ★ WHAT IS LEFT IN `remaining` IS REVENUE THE LADDER STATES NO RATE FOR. The walk used to
        // drop it on the floor and hand back a total that looked like an ordinary commission.
        return new LadderWalk(total, remaining > 0m ? remaining : 0m);
    }

    /// <summary>
    /// The bracket containing <paramref name="attainmentPct"/>, or null when the ladder does not
    /// mention this ratio at all.
    ///
    /// ★ LastOrDefault IS THE OVERLAP RULE FOR THIS PATH, and it is deliberate: a ratio on a shared
    /// edge (attainment 1.00 under [0.50,1.00] and [1.00,null]) belongs to the upper tier, and any
    /// ratio covered twice is priced by exactly one bracket, never by two rates added together. The
    /// split walk reaches the same answer by clipping; see <see cref="WalkSplitTiers"/>.
    /// </summary>
    internal static AttainmentTier? FindAttainmentBracket(
        IReadOnlyList<AttainmentTier> tiers,
        decimal attainmentPct)
        => tiers.LastOrDefault(t =>
            t.AttainmentFrom <= attainmentPct &&
            (t.AttainmentTo == null || attainmentPct <= t.AttainmentTo.Value));

    internal static Money ComputeAttainmentCommission(
        Money baseAmount,
        IReadOnlyList<AttainmentTier> tiers,
        decimal attainmentPct)
    {
        var tier = FindAttainmentBracket(tiers, attainmentPct);

        // ★ THE CALLER DECIDES WHAT "NO BRACKET" MEANS, NOT THIS METHOD. This zero is arithmetic —
        // there is no rate to multiply by — and it is NOT the engine's answer: ComputeRate refuses
        // before it ever gets here (KAN-26 tanda 3). The behaviour is left as it was so that every
        // caller of this pure function, including the frozen expectations in the characterization
        // suites, keeps seeing the same number.
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
        => WalkSplitTiers(txAmount, tiers, priorCumulative, quotaTarget, tierTrace).Commission;

    /// <summary>
    /// The split walk, with the two things the old one threw away: how much of the transaction no
    /// tier priced, and the guarantee that no euro was priced twice.
    ///
    /// ★★ EACH TIER'S CEILING IS CLIPPED TO THE NEXT FLOOR ABOVE IT, AND THAT IS THE OVERLAP FIX.
    /// The walk used to give every tier its own range outright, so a ladder whose tiers all run to
    /// infinity — rule A1CDBEA0, three open tiers at 5%, 8% and 9% — charged all three rates over
    /// the same revenue and paid a blended 22% on a table that declares 9%. Clipping makes the
    /// ranges disjoint, so every euro is priced by EXACTLY ONE tier, which is the same rule the
    /// bracket lookup enforces with LastOrDefault: where two tiers claim the same revenue, the
    /// higher one wins.
    ///
    /// ★ ON A VALID LADDER THIS IS A NO-OP. Tiers that touch exactly already have
    /// <c>ceiling == next floor</c>, so the clip changes nothing and every existing amount is
    /// reproduced to the cent. It only ever bites on a table that overlaps, which is a table the
    /// write path has refused since the ladder invariants landed.
    /// </summary>
    internal static LadderWalk WalkSplitTiers(
        Money txAmount,
        IReadOnlyList<AttainmentTier> tiers,
        decimal priorCumulative,
        decimal quotaTarget,
        List<RateTierStep>? tierTrace = null)
    {
        // A target of zero has no ladder to project onto — every boundary collapses to 0. The whole
        // transaction is unpriced, and the caller reads that as a refusal rather than as a zero.
        if (quotaTarget <= 0m)
            return new LadderWalk(Money.Zero(txAmount.Currency), txAmount.Amount);

        var txValue = txAmount.Amount;
        var txStart = priorCumulative;
        var txEnd = priorCumulative + txValue;
        var total = 0m;
        var priced = 0m;

        for (var i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            var tierFloor = tier.AttainmentFrom * quotaTarget;
            var tierCeiling = tier.AttainmentTo.HasValue
                ? tier.AttainmentTo.Value * quotaTarget
                : decimal.MaxValue;

            // The lowest floor of any tier above this one. On a well-formed ladder that is the next
            // tier's floor, which already equals this tier's ceiling.
            for (var j = i + 1; j < tiers.Count; j++)
            {
                var higherFloor = tiers[j].AttainmentFrom * quotaTarget;
                if (higherFloor < tierCeiling) tierCeiling = higherFloor;
            }

            var overlapStart = Math.Max(txStart, tierFloor);
            var overlapEnd = Math.Min(txEnd, tierCeiling);

            if (overlapEnd > overlapStart)
            {
                var portion = overlapEnd - overlapStart;
                total += portion * tier.Rate;
                priced += portion;
                tierTrace?.Add(new RateTierStep(
                    tier.AttainmentFrom, tier.AttainmentTo, tier.Rate,
                    portion, Money.Of(portion * tier.Rate, txAmount.Currency)));
            }
        }

        // ★ THE RANGES ARE DISJOINT AFTER CLIPPING, so `priced` can never exceed the transaction and
        // this subtraction is the revenue the ladder genuinely never mentions — whether it sits
        // above a bounded top tier or inside a gap between two.
        var unpriced = txValue - priced;
        return new LadderWalk(Money.Of(total, txAmount.Currency), unpriced > 0m ? unpriced : 0m);
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
        var rate = ComputeRate(
            rule, tx, baseAmount, planCurrency, attainmentPct, splitContext,
            logger, trace, attainmentSource);

        var commission = rate.Commission;

        // ── 4/5/6. Modifier, then cap, then floor ────────────────────────────
        //
        // ★★ ONLY THE FLOOR NEEDS THE REFUSAL, AND THAT IS NOT A SHORTCUT — IT IS ARITHMETIC.
        // Every modifier type multiplies (Accelerator, Multiplier and the Spiff stub all go through
        // ApplyModifier's single Multiply), and a cap only ever lowers. Both leave a zero a zero. The
        // floor is the one component that can LIFT, so it is the one that could undo a refusal, and
        // suppressing it is enough to make "the credit is zero" true rather than nearly true.
        commission = TraceModifier(commission, baseAmount, rule.Modifier, trace);
        commission = TraceCap(commission, rule.Cap, trace);
        commission = TraceFloor(commission, rule.Floor, trace, rate.Refused);

        return new RuleEvaluation(true, baseAmount, commission);
    }

    /// <summary>
    /// What the Rate component produced, and whether it REFUSED to produce anything.
    ///
    /// ★★ THE FLAG EXISTS SO THE FLOOR CANNOT UNDO THE REFUSAL. A floor is the minimum commission on
    /// a commissioned sale — a component OF the calculation, which presupposes there was one. When
    /// the rule cannot calculate at all, there is no commission for a floor to be the minimum of, and
    /// a floor paying anyway is an orphan, not a guarantee. (A DRAW — a guaranteed income floor that
    /// pays regardless of sales — is a different mechanism, at rep and period level rather than per
    /// transaction, and is not this engine's business.)
    ///
    /// ★★ IT IS ONE FLAG FOR ALL FOUR REFUSALS, AND THAT IS DELIBERATE. Tanda 2 introduced it for
    /// "nobody set a quota"; tanda 3 adds "the ladder states no rate for this ratio" and "the ladder
    /// stops below this amount". The reason differs and the trace records which one
    /// (<c>RateRefusalReason</c>), but the consequence is identical — there is no commission — so
    /// splitting it into several booleans would only create the chance of honouring one and
    /// forgetting another.
    ///
    /// ★ IT IS CARRIED, NOT RE-DERIVED. Evaluate could work the question out again from the rule and
    /// the source, but then the predicate would live in two places and they would drift; the
    /// component that made the decision is the one that reports it.
    /// </summary>
    private readonly record struct RateOutcome(Money Commission, bool Refused);

    private static RateOutcome ComputeRate(
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
                return new RateOutcome(zero, false);
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
            return new RateOutcome(units, false);
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
                    // ★ THE REASON THE COMMISSION IS ZERO, not just the fact. A null split context IS
                    // "no quota in effect" — the log line above says so — so the trace says NoTarget
                    // rather than leaving the reader to conclude the rep sold nothing.
                    AttainmentSource = AttainmentSource.NoTarget,
                    RateRefusal = RateRefusalReason.NoQuotaInEffect,
                });
                return new RateOutcome(zero, Refused: true);
            }

            // ★★ A QUOTA THAT EXISTS AND TARGETS ZERO IS ALSO "NOTHING TO MEASURE AGAINST", AND THIS
            // BRANCH IS WHY IT NEEDED SAYING TWICE. QuotaAttainmentService already calls a zero
            // target the second way to have no target and returns NoTarget on the BRACKET path — but
            // GetSplitContextAsync hands back a context without looking at the target
            // (QuotaAttainmentService.cs:135-139), so the split walk got a live context with a zero
            // in it, projected every tier boundary onto zero and returned a zero stamped Applied +
            // Measured. Same hole as the null context, one line further on.
            if (splitContext.QuotaTarget <= 0m)
            {
                logger?.LogWarning(
                    "Split-at-quota: the quota in effect for payee={PayeeId}, plan={PlanId}, " +
                    "date={Date} targets zero, so there is no ladder to split at. Commission set to " +
                    "zero and the step marked Skipped. Set a non-zero target to earn under this rule.",
                    tx.PayeeId, rule.PlanId, tx.TransactionDate);

                var zeroTarget = Money.Zero(baseAmount.Currency);
                trace?.Add(new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Rate,
                    Outcome = RuleCalculationOutcome.Skipped,
                    Input = baseAmount,
                    Output = zeroTarget,
                    RateTable = rule.RateTable.Type,
                    AttainmentSource = AttainmentSource.NoTarget,
                    RateRefusal = RateRefusalReason.NoQuotaInEffect,
                });
                return new RateOutcome(zeroTarget, Refused: true);
            }

            List<RateTierStep>? splitTiers = trace is null ? null : new List<RateTierStep>();
            var splitWalk = WalkSplitTiers(
                baseAmount, rule.RateTable.AttainmentTiers!,
                splitContext.PriorCumulative, splitContext.QuotaTarget, splitTiers);

            // The attainment this walk actually used, as a ratio of the quota — the same figure the
            // bracket path puts here, so one field answers "what percentage was used" on both
            // attainment paths instead of only one.
            var splitOperand = Math.Round(
                splitContext.PriorCumulative / splitContext.QuotaTarget, 4, MidpointRounding.ToEven);

            // ★★ PATH (c). The transaction ran past the top of the ladder, or fell in a gap, and the
            // walk priced only part of it. Paying the covered slice is the most dangerous shape this
            // family of bugs takes: it does not look like a failure, it looks like a small
            // commission. So the whole transaction is refused rather than part-paid — the tiers that
            // DID match stay in the trace, so the reader can see exactly how far the ladder got.
            if (splitWalk.Unpriced > 0m)
            {
                logger?.LogWarning(
                    "Rule {RuleId}: the split-at-quota ladder prices only part of this transaction — " +
                    "{Unpriced} of {Total} {Currency} falls outside every tier (quota={Quota}, " +
                    "prior={Prior}). Commission set to zero and the step marked Skipped: the engine " +
                    "will not pay a slice of a sale as if it were the whole one. Extend the top tier " +
                    "or close the gap in the ladder.",
                    rule.Id, splitWalk.Unpriced, baseAmount.Amount, baseAmount.Currency,
                    splitContext.QuotaTarget, splitContext.PriorCumulative);

                var unpricedZero = Money.Zero(baseAmount.Currency);
                trace?.Add(new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Rate,
                    Outcome = RuleCalculationOutcome.Skipped,
                    Input = baseAmount,
                    Output = unpricedZero,
                    RateTable = rule.RateTable.Type,
                    Tiers = splitTiers,
                    AttainmentSource = AttainmentSource.Measured,
                    Operand = splitOperand,
                    RateRefusal = RateRefusalReason.AmountOutsideTable,
                });
                return new RateOutcome(unpricedZero, Refused: true);
            }

            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Applied,
                Input = baseAmount,
                Output = splitWalk.Commission,
                RateTable = rule.RateTable.Type,
                Tiers = splitTiers,
                // A split context only exists when a real quota answered, so this walk was measured.
                // The step used to omit the source entirely, which read as "not an attainment rule".
                AttainmentSource = AttainmentSource.Measured,
                Operand = splitOperand,
            });
            return new RateOutcome(splitWalk.Commission, false);
        }

        if (rule.RateTable.Type == RateTableType.Tiered)
        {
            List<RateTierStep>? walked = trace is null ? null : new List<RateTierStep>();
            var tieredWalk = WalkTiers(baseAmount, rule.RateTable.Tiers!, walked);

            // ★★ PATH (b), AND THE ONE THAT IS LIVE RIGHT NOW. Every tiered rule in this database
            // has a bounded top tier, two of them on Active plans: "RL-1" stops at 10,000, so a
            // 1,000,000 EUR sale used to pay 820 EUR — the same as a 10,000 EUR sale — with the step
            // marked Applied. The excess was not capped, which is a decision somebody could make and
            // audit; it was dropped, which is not.
            //
            // ★ THE RULE STOPS PAYING ENTIRELY RATHER THAN PAYING WHAT IT CAN. Half an answer here
            // is indistinguishable from a whole one, and this engine's job is to be auditable before
            // it is generous: a zero with a reason is a case a human resolves, a plausible number is
            // a case nobody ever looks at. A rule that genuinely means to stop paying above a
            // threshold expresses that with a CAP, which the cascade honours a few lines below.
            if (tieredWalk.Unpriced > 0m)
            {
                logger?.LogWarning(
                    "Rule {RuleId}: the tiered ladder prices only part of this transaction — " +
                    "{Unpriced} of {Total} {Currency} falls above the last tier or inside a gap. " +
                    "Commission set to zero and the step marked Skipped: the engine will not pay the " +
                    "covered slice as if it were the whole sale. Open the last tier (or add a cap if " +
                    "the ceiling is intentional).",
                    rule.Id, tieredWalk.Unpriced, baseAmount.Amount, baseAmount.Currency);

                var unpricedZero = Money.Zero(baseAmount.Currency);
                trace?.Add(new RuleCalculationStep
                {
                    Component = RuleCalculationComponent.Rate,
                    Outcome = RuleCalculationOutcome.Skipped,
                    Input = baseAmount,
                    Output = unpricedZero,
                    RateTable = rule.RateTable.Type,
                    Tiers = walked,
                    RateRefusal = RateRefusalReason.AmountOutsideTable,
                });
                return new RateOutcome(unpricedZero, Refused: true);
            }

            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Applied,
                Input = baseAmount,
                Output = tieredWalk.Commission,
                RateTable = rule.RateTable.Type,
                Tiers = walked,
            });
            return new RateOutcome(tieredWalk.Commission, false);
        }

        // ── NoTarget: an attainment rule with nothing to measure against ─────
        //
        // ★★ THIS IS THE PATH THAT PAID 7,160 EUR FOR QUOTAS NOBODY EVER SET. An attainment lookup
        // with no quota resolves the ratio to 0, 0 falls inside the [0,1] bracket, and the engine
        // paid that bracket's rate on the whole sale and stamped the step Applied — indistinguishable
        // from a rep who genuinely achieved 0%. Two credits reached a Paid payout that way.
        //
        // ★★ THE RATIO CANNOT TELL THE TWO APART; ONLY THE SOURCE CAN. "Achieved none of a real
        // target" and "there was no target" are both 0. That is exactly why KAN-27 made the source
        // travel beside the ratio instead of leaving it to be inferred — and this is the decision
        // that could not be made before it existed.
        //
        // ★ IT REFUSES, IT DOES NOT GUESS. An attainment rule presupposes a quota; without one the
        // configuration is incomplete, not a case to calculate. The engine does not invent a number
        // and does not fall back to the base tier: it pays zero, says why in the trace, and hands the
        // case back to a human to set the quota or confirm the exception. Registering is not throwing
        // (§B1): an exception here would kill the whole pay-run batch, and this leaves a row somebody
        // can find instead.
        //
        // ★ THE SIBLING BRANCH ALREADY DID THIS. Split-at-quota with no context returns zero with
        // Skipped + NoTarget a few lines above. The asymmetry was never intentional; this is the
        // bracket path catching up, using the same outcome so one query finds both.
        //
        // Skipped and not NotConfigured on purpose: the rule DOES configure a rate table — what is
        // missing is the quota — and NotConfigured means "the rule does not configure this component
        // at all". Skipped is documented as "configured, but the engine declined to apply it", which
        // is precisely what happened.
        if (rule.RateTable.Type == RateTableType.AttainmentBased &&
            attainmentSource == AttainmentSource.NoTarget)
        {
            logger?.LogWarning(
                "Attainment rule {RuleId}: no quota in effect for payee={PayeeId}, plan={PlanId}, " +
                "date={Date}. Commission set to zero and the step marked Skipped — the rule pays by " +
                "attainment and there is no target to measure against. Assign a quota to earn " +
                "commission under this rule.",
                rule.Id, tx.PayeeId, rule.PlanId, tx.TransactionDate);

            var noTarget = Money.Zero(baseAmount.Currency);
            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Skipped,
                Input = baseAmount,
                Output = noTarget,
                RateTable = rule.RateTable.Type,
                AttainmentSource = AttainmentSource.NoTarget,
                // ★★ THE RATIO STILL TRAVELS, AND OMITTING IT WAS A MISTAKE THIS TEST SUITE CAUGHT.
                // The first cut left Operand null, reasoning that publishing a 0 the engine refuses
                // to trust would read as a measured "0%". That breaks KAN-27's contract, which is
                // the opposite: the ratio alone is what lies, and the SOURCE beside it is what makes
                // it safe to publish. The pair (0, NoTarget) is the honest representation; a null
                // just deletes half of it and tells the reader less.
                Operand = attainmentPct,
                RateRefusal = RateRefusalReason.NoQuotaInEffect,
            });
            return new RateOutcome(noTarget, Refused: true);
        }

        // ── Path (a): a bracket ladder that does not mention this ratio ──────
        //
        // ★★ THE LOOKUP USED TO ANSWER "NO BRACKET" WITH A ZERO, AND ZERO IS A RATE SOMEBODY MIGHT
        // HAVE CHOSEN. "This ladder pays nothing at 30% attainment" and "this ladder never says what
        // 30% attainment is worth" are a policy and a hole, they arrive as the same number, and the
        // step said Applied for both.
        //
        // ★ IT IS REACHABLE THROUGH TODAY'S WRITE PATH, which is why it is not merely a legacy-row
        // guard. Nothing in ValidateLadder requires a ladder to START at zero: [0.5→1, 1→open] is
        // strictly ascending, closed except for the last, touching, and every rate a fraction — it
        // saves cleanly, and every rep under half quota falls off the bottom of it.
        //
        // ★ NOT reached by the split path: that one projects tiers onto revenue and reports its hole
        // as unpriced amount instead. Nor by a well-formed ladder from 0 with an open top, which
        // contains every ratio there is — this branch cannot fire on a table that covers its subject.
        if (rule.RateTable.Type == RateTableType.AttainmentBased &&
            FindAttainmentBracket(rule.RateTable.AttainmentTiers!, attainmentPct) is null)
        {
            logger?.LogWarning(
                "Rule {RuleId}: attainment {Attainment} falls outside every tier of this rule's " +
                "ladder for payee={PayeeId}, plan={PlanId}, date={Date}. Commission set to zero and " +
                "the step marked Skipped — the table states no rate for this attainment, which is " +
                "not the same as stating a rate of zero. Extend the ladder to cover it.",
                rule.Id, attainmentPct, tx.PayeeId, rule.PlanId, tx.TransactionDate);

            var noBracket = Money.Zero(baseAmount.Currency);
            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Rate,
                Outcome = RuleCalculationOutcome.Skipped,
                Input = baseAmount,
                Output = noBracket,
                RateTable = rule.RateTable.Type,
                // The ratio still travels, for the same reason it does on the NoTarget step: it is
                // the figure the reader needs in order to see WHICH value the ladder failed to cover.
                Operand = attainmentPct,
                AttainmentSource = attainmentSource,
                RateRefusal = RateRefusalReason.NoMatchingBracket,
            });
            return new RateOutcome(noBracket, Refused: true);
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
        return new RateOutcome(result, false);
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

    /// <param name="refused">
    /// True when the Rate component declined to calculate at all — no quota in effect, no bracket
    /// containing this attainment, or a ladder that prices only part of the transaction. The floor
    /// is then not applied: it is the minimum of a commission, and there was no commission.
    ///
    /// ★ THE STEP STILL APPEARS, AND IT SAYS Skipped. Dropping it — or reporting NotConfigured —
    /// would hide a floor the rule really does carry, and somebody auditing a zero on a rule with an
    /// 8,520 EUR floor has to be able to see that the floor was CONSULTED AND DECLINED rather than
    /// wonder whether the engine forgot it (§B1: register, never fail quietly).
    /// </param>
    private static Money TraceFloor(
        Money commission, Floor? floor, List<RuleCalculationStep>? trace,
        bool refused = false)
    {
        if (refused)
        {
            trace?.Add(new RuleCalculationStep
            {
                Component = RuleCalculationComponent.Floor,
                Outcome = floor is null
                    ? RuleCalculationOutcome.NotConfigured
                    : RuleCalculationOutcome.Skipped,
                Input = commission,
                Output = commission,
                Threshold = floor?.Amount,
            });
            return commission;
        }

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
