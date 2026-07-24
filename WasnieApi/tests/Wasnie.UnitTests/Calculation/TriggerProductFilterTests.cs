using FluentAssertions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Validators.Plans;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// The trigger as a real eligibility filter — the surface that decides which plan a sale belongs to.
///
/// Mutual exclusion between plans is expressed HERE ("this plan takes laptops, that one takes
/// monitors"), so a filter that quietly never fires is a rep who quietly never gets paid. Every rule
/// with a condition in the tenant is in that state today: two name a field the engine cannot read,
/// two compare a field against a value it can never hold.
/// </summary>
public sealed class TriggerProductFilterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    private static CompensationTransaction Tx(string? sku, decimal amount = 1000m) =>
        CompensationTransaction.Ingest(
            TenantId, $"REF-{Guid.NewGuid():N}", Guid.NewGuid(), Money.Of(amount, "EUR"),
            new DateOnly(2026, 6, 15), TransactionSource.CrmSync, "sync",
            Guid.NewGuid(), Now, Guid.NewGuid(), productSku: sku);

    private static Trigger Single(string field, ConditionOperator op, string raw = "", string[]? set = null) =>
        Trigger.When(LogicalOperator.And,
        [
            new Condition
            {
                Field = field,
                Operator = op,
                Value = new ConditionValue { Type = ConditionValueType.String, Raw = raw, Set = set },
            },
        ]);

    // (a) Exact match on the real tenant SKUs.
    [Fact]
    public void Equal_on_productsku_fires_only_for_that_product()
    {
        var trigger = Single("productsku", ConditionOperator.Equal, raw: "LAP-12");

        CommissionCalculator.EvaluateTrigger(trigger, Tx("LAP-12")).Should().BeTrue();
        CommissionCalculator.EvaluateTrigger(trigger, Tx("DELL-pol")).Should().BeFalse();
    }

    // The field name is matched case-insensitively, as the engine always did.
    [Fact]
    public void The_field_name_is_case_insensitive()
    {
        var trigger = Single("ProductSku", ConditionOperator.Equal, raw: "LAP-12");

        CommissionCalculator.EvaluateTrigger(trigger, Tx("LAP-12")).Should().BeTrue();
    }

    // (b) In over a set — the operator the UI could never reach because it always sent set: null.
    [Fact]
    public void In_fires_for_every_sku_in_the_set_and_no_other()
    {
        var trigger = Single("productsku", ConditionOperator.In, set: ["LAP-12", "DELL-pol"]);

        CommissionCalculator.EvaluateTrigger(trigger, Tx("LAP-12")).Should().BeTrue();
        CommissionCalculator.EvaluateTrigger(trigger, Tx("DELL-pol")).Should().BeTrue();
        CommissionCalculator.EvaluateTrigger(trigger, Tx("DELL-pol SINGLE")).Should().BeFalse();
    }

    // (c) NotIn is the complement — this is how one plan excludes what another one takes.
    [Fact]
    public void NotIn_is_the_complement_of_the_set()
    {
        var trigger = Single("productsku", ConditionOperator.NotIn, set: ["LAP-12"]);

        CommissionCalculator.EvaluateTrigger(trigger, Tx("LAP-12")).Should().BeFalse();
        CommissionCalculator.EvaluateTrigger(trigger, Tx("DELL-pol")).Should().BeTrue();
    }

    // (f) A deal-level row has no SKU. The condition must simply not match — not throw.
    [Fact]
    public void A_transaction_without_a_sku_does_not_match_and_does_not_throw()
    {
        var trigger = Single("productsku", ConditionOperator.Equal, raw: "LAP-12");

        CommissionCalculator.EvaluateTrigger(trigger, Tx(null)).Should().BeFalse();
    }

    // (g) No regression: the fields that already worked still do.
    [Fact]
    public void Existing_conditions_on_source_and_amount_still_behave_the_same()
    {
        var source = Single("source", ConditionOperator.Equal, raw: "CrmSync");
        CommissionCalculator.EvaluateTrigger(source, Tx("LAP-12")).Should().BeTrue();

        var amount = Trigger.When(LogicalOperator.And,
        [
            new Condition
            {
                Field = "transactionamount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" },
            },
        ]);
        CommissionCalculator.EvaluateTrigger(amount, Tx("LAP-12", amount: 1000m)).Should().BeTrue();
        CommissionCalculator.EvaluateTrigger(amount, Tx("LAP-12", amount: 100m)).Should().BeFalse();
    }

    // An unknown field cannot match — the state every conditioned rule in the tenant is in today.
    [Fact]
    public void An_unknown_field_never_matches()
    {
        var trigger = Single("DealType", ConditionOperator.Equal, raw: "New Logo");

        CommissionCalculator.EvaluateTrigger(trigger, Tx("LAP-12")).Should().BeFalse();
    }

    // ── Save-time validation: the fix that stops mute rules being created at all ───────────────

    private static bool IsValid(Trigger trigger) => new TriggerValidator().Validate(trigger).IsValid;

    // (d) A field outside the catalog is rejected instead of saving silently.
    [Fact]
    public void Saving_a_condition_on_an_unknown_field_is_rejected()
    {
        IsValid(Single("DealType", ConditionOperator.Equal, raw: "New Logo")).Should().BeFalse();
    }

    // (e) In without a set matches nothing; NotIn without a set matches everything. Both are wrong.
    [Fact]
    public void Saving_In_without_a_set_is_rejected()
    {
        IsValid(Single("productsku", ConditionOperator.In)).Should().BeFalse();
        IsValid(Single("productsku", ConditionOperator.In, set: [])).Should().BeFalse();
        IsValid(Single("productsku", ConditionOperator.NotIn, set: ["  "])).Should().BeFalse();
    }

    [Fact]
    public void Saving_a_non_set_operator_without_a_value_is_rejected()
    {
        IsValid(Single("productsku", ConditionOperator.Equal, raw: "")).Should().BeFalse();
    }

    // An operator the field's evaluator does not implement would never fire — reject it too.
    [Fact]
    public void Saving_an_operator_the_field_does_not_support_is_rejected()
    {
        // Ordering on a string field: EvaluateString has no GreaterThan.
        IsValid(Single("productsku", ConditionOperator.GreaterThan, raw: "LAP-12")).Should().BeFalse();

        // A set on a numeric field: EvaluateNumeric has no In.
        IsValid(Single("transactionamount", ConditionOperator.In, set: ["100"])).Should().BeFalse();
    }

    [Fact]
    public void A_well_formed_condition_is_accepted()
    {
        IsValid(Single("productsku", ConditionOperator.In, set: ["LAP-12", "DELL-pol"])).Should().BeTrue();
        IsValid(Single("productsku", ConditionOperator.Equal, raw: "LAP-12")).Should().BeTrue();
    }

    // ── The catalog is the single source of truth ─────────────────────────────────────────────

    // Every offered field must be readable by the engine, and every offered operator honoured by the
    // evaluator for its type. This is what stops the UI ever promising a filter that does nothing.
    [Fact]
    public void Every_catalog_field_resolves_and_only_offers_honoured_operators()
    {
        var tx = Tx("LAP-12");

        foreach (var definition in TriggerFieldCatalog.Fields)
        {
            TriggerFieldCatalog.Find(definition.Field).Should().NotBeNull();
            definition.Operators.Should().NotBeEmpty($"{definition.Field} must offer some operator");

            // Resolve must not throw for any field (null is a valid "no value").
            var act = () => definition.Resolve(tx);
            act.Should().NotThrow();

            definition.Operators.Should().BeEquivalentTo(
                TriggerFieldCatalog.OperatorsFor(definition.ValueType));
        }
    }

    [Fact]
    public void The_product_fields_are_in_the_catalog()
    {
        TriggerFieldCatalog.Find("productsku").Should().NotBeNull();
        TriggerFieldCatalog.Find("productname").Should().NotBeNull();
        TriggerFieldCatalog.Find("productsku")!.ValueType.Should().Be(ConditionValueType.String);
    }
}
