using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// How a rule's CONFIGURATION is described to the model, in one place.
///
/// ★★ EXTRACTED SO TWO TOOLS CANNOT DRIFT. `get_plan_rules` answers "how is this configured" and
/// `simulate_plan_rules` answers "what would it pay"; the second now carries the first, so a single
/// call covers a compound question. Two copies of this projection would be two descriptions of the
/// same rule, and the day they disagreed the model would have both in one context.
///
/// ★ IT MOVED UNCHANGED. Every method and record below is the one `GetPlanRulesTool` already used —
/// visibility widened, nothing rewritten. `PlanRulesPayloadCompletenessTests` is what says so.
/// </summary>
public static class PlanRuleProjection
{
    public enum TriggerToken { Unconditional }

    public enum FieldStatusToken { Recognised, UnknownFieldRuleNeverMatches }

    public enum ModifierSemanticToken { MultipliesCommissionByFactor }

    public enum ModifierConditionToken { Unconditional, ConditionsIgnoredModifierAlwaysApplies }

    public enum LimitEnforcementToken
    {
        EnforcedPerTransaction,
        NotEnforcedScopeNotImplemented,
        NotEnforcedCurrencyMismatch,
    }

    public sealed record PlanRule(
        string RuleName,
        int SortOrder,
        object TriggerCondition,
        string MeasurementType,
        MeasurementBase MeasurementBase,
        RateTableDescription RateTable,
        IReadOnlyList<ModifierDescription> Modifiers,
        LimitDescription? Cap,
        LimitDescription? Floor);

    public sealed record TriggerConditions(
        string LogicalOperator, IReadOnlyList<TriggerCondition> Conditions);

    public sealed record TriggerCondition(
        string Field,
        string Operator,
        string ValueType,
        string? Value,
        IReadOnlyList<string>? Values,
        string FieldStatus);

    public sealed record RateTableDescription(
        string Type,
        RateSemantic SemanticBehavior,
        decimal? RawValue,
        IReadOnlyList<AmountTier>? AmountTiers,
        IReadOnlyList<AttainmentBracket>? AttainmentTiers,
        bool? SplitAtQuota);

    public sealed record AmountTier(decimal FromAmount, decimal? ToAmount, decimal RawRate);

    public sealed record AttainmentBracket(
        decimal FromAttainmentFraction, decimal? ToAttainmentFraction, decimal RawRate);

    public sealed record ModifierDescription(
        string Type, decimal Factor, string SemanticBehavior, string ConditionHandling);

    public sealed record LimitDescription(
        decimal RawAmount, string CurrencyCode, string? Scope, string Enforcement);

    public static PlanRule DescribeRule(RuleDto rule, string planCurrency)
    {
        // RuleDto carries the domain value objects as `object` (it is the shape the plans screen
        // consumes). A cast that fails means the mapper changed underneath this tool, which is a fault
        // worth failing loudly for — not a plan to describe with pieces missing.
        var trigger = Cast<Trigger>(rule.Trigger, nameof(rule.Trigger), rule.Name);
        var measurement = Cast<Measurement>(rule.Measurement, nameof(rule.Measurement), rule.Name);
        var rateTable = Cast<RateTable>(rule.RateTable, nameof(rule.RateTable), rule.Name);
        var modifier = rule.Modifier is null ? null : Cast<Modifier>(rule.Modifier, nameof(rule.Modifier), rule.Name);
        var cap = rule.Cap is null ? null : Cast<Cap>(rule.Cap, nameof(rule.Cap), rule.Name);
        var floor = rule.Floor is null ? null : Cast<Floor>(rule.Floor, nameof(rule.Floor), rule.Name);

        return new PlanRule(
            RuleName: rule.Name,
            SortOrder: rule.SortOrder,
            TriggerCondition: DescribeTrigger(trigger),
            MeasurementType: measurement.Type.ToString(),
            MeasurementBase: PlanRuleSemantics.BaseOf(measurement.Type),
            RateTable: DescribeRateTable(rateTable, measurement.Type),
            // The domain holds at MOST ONE modifier per rule. It is emitted as a list because the
            // contract is a list and a second modifier is a schema change, not a payload change.
            Modifiers: modifier is null ? [] : [DescribeModifier(modifier)],
            Cap: DescribeCap(cap, planCurrency),
            Floor: DescribeFloor(floor, planCurrency));
    }

    private static T Cast<T>(object value, string field, string ruleName) where T : class =>
        value as T ?? throw new InvalidOperationException(
            $"Rule '{ruleName}' returned a {field} of type {value.GetType().Name}; expected {typeof(T).Name}.");

    /// <summary>
    /// ★ "Unconditional" IS AN ABSOLUTE TOKEN, not the phrase "all transactions". The model translates
    /// it; a backend that shipped English here would ship English to the Spanish and Polish users too.
    /// </summary>
    private static object DescribeTrigger(Trigger trigger)
    {
        if (trigger.Conditions.Count == 0)
        {
            return nameof(TriggerToken.Unconditional);
        }

        return new TriggerConditions(
            trigger.LogicalOperator.ToString(),
            trigger.Conditions.Select(c => new TriggerCondition(
                Field: c.Field,
                Operator: c.Operator.ToString(),
                ValueType: c.Value.Type.ToString(),
                Value: c.Value.Set is { Count: > 0 } ? null : c.Value.Raw,
                Values: c.Value.Set is { Count: > 0 } ? c.Value.Set : null,
                // ★ A RULE THAT CAN NEVER FIRE IS THE ANSWER TO "why was I not paid". The engine
                // resolves a condition's field through TriggerFieldCatalog and treats an unknown name as
                // "does not match" — for ever. Reporting the condition without this would have the
                // assistant describe a rule that pays, about a rule that cannot.
                FieldStatus: TriggerFieldCatalog.Find(c.Field) is null
                    ? nameof(FieldStatusToken.UnknownFieldRuleNeverMatches)
                    : nameof(FieldStatusToken.Recognised))).ToList());
    }

    private static RateTableDescription DescribeRateTable(RateTable table, MeasurementType measurement)
    {
        var semantic = PlanRuleSemantics.Describe(table.Type, measurement, table.SplitAtQuota);

        return new RateTableDescription(
            Type: table.Type.ToString(),
            SemanticBehavior: semantic,
            RawValue: table.FlatRate,
            AmountTiers: table.Tiers?.Select(t => new AmountTier(t.From, t.To, t.Rate)).ToList(),
            AttainmentTiers: table.AttainmentTiers?
                .Select(t => new AttainmentBracket(t.AttainmentFrom, t.AttainmentTo, t.Rate)).ToList(),
            SplitAtQuota: table.Type == RateTableType.AttainmentBased ? table.SplitAtQuota : null);
    }

    /// <summary>
    /// ★ THE MODIFIER'S OWN CONDITIONS ARE IGNORED BY THE ENGINE, and that is reported rather than
    /// hidden. <c>CommissionCalculator.ApplyModifier</c> multiplies by the factor unconditionally — it
    /// never evaluates <c>Modifier.Trigger</c>. An administrator who configured a conditional
    /// accelerator has one that always applies, and the assistant describing the intent instead of the
    /// behaviour would confirm a belief that is costing money on every transaction.
    ///
    /// All three modifier types (Accelerator, Multiplier, Spiff) do the same thing today — Spiff is a
    /// stub that multiplies. One semantic token, three names.
    /// </summary>
    private static ModifierDescription DescribeModifier(Modifier modifier) =>
        new(
            Type: modifier.Type.ToString(),
            Factor: modifier.Factor,
            SemanticBehavior: nameof(ModifierSemanticToken.MultipliesCommissionByFactor),
            ConditionHandling: modifier.Trigger is { Conditions.Count: > 0 }
                ? nameof(ModifierConditionToken.ConditionsIgnoredModifierAlwaysApplies)
                : nameof(ModifierConditionToken.Unconditional));

    /// <summary>
    /// ★ A CAP THE ENGINE DOES NOT ENFORCE IS THE MOST DANGEROUS FIELD ON A PLAN. Only
    /// <c>PerTransaction</c> is honoured; <c>PerPeriod</c> and <c>Total</c> are accepted by the model and
    /// skipped by <c>CommissionCalculator.ApplyCap</c>, and a cap in a currency other than the
    /// commission's is skipped too. Telling a user "your plan is capped at €500" when nothing enforces
    /// it is worse than saying nothing, so the enforcement state travels with the amount.
    ///
    /// The commission is denominated in the plan's currency (the engine rejects a transaction whose
    /// currency differs), so comparing the cap against the PLAN currency is the same comparison the
    /// engine makes at calculation time.
    /// </summary>
    private static LimitDescription? DescribeCap(Cap? cap, string planCurrency)
    {
        if (cap is null) return null;

        var enforcement =
            cap.Scope != CapScope.PerTransaction
                ? nameof(LimitEnforcementToken.NotEnforcedScopeNotImplemented)
                : !string.Equals(cap.Amount.Currency, planCurrency, StringComparison.OrdinalIgnoreCase)
                    ? nameof(LimitEnforcementToken.NotEnforcedCurrencyMismatch)
                    : nameof(LimitEnforcementToken.EnforcedPerTransaction);

        return new LimitDescription(cap.Amount.Amount, cap.Amount.Currency, cap.Scope.ToString(), enforcement);
    }

    /// <summary>A floor has no scope — it is always per transaction — but the currency skip applies.</summary>
    private static LimitDescription? DescribeFloor(Floor? floor, string planCurrency)
    {
        if (floor is null) return null;

        var enforcement =
            !string.Equals(floor.Amount.Currency, planCurrency, StringComparison.OrdinalIgnoreCase)
                ? nameof(LimitEnforcementToken.NotEnforcedCurrencyMismatch)
                : nameof(LimitEnforcementToken.EnforcedPerTransaction);

        return new LimitDescription(floor.Amount.Amount, floor.Amount.Currency, Scope: null, enforcement);
    }
}
