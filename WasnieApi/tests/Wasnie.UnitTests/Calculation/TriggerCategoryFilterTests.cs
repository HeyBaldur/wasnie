using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// The trigger filtering on the ENRICHED category — the stable, discrete field a rule should key off
/// instead of the raw origin column. Proves the category rule fires for a categorized transaction and,
/// critically, that an uncategorized transaction is NOT blocked from rules that don't mention category.
/// </summary>
public sealed class TriggerCategoryFilterTests
{
    private static CompensationTransaction Tx(string? category, decimal amount = 1000m) =>
        CompensationTransaction.Ingest(
            Guid.NewGuid(), $"REF-{Guid.NewGuid():N}", Guid.NewGuid(), Money.Of(amount, "EUR"),
            new DateOnly(2026, 7, 1), TransactionSource.CrmSync, "sync",
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), category: category);

    private static Trigger CategoryIn(params string[] set) =>
        Trigger.When(LogicalOperator.And,
        [
            new Condition
            {
                Field = "category",
                Operator = ConditionOperator.In,
                Value = new ConditionValue { Type = ConditionValueType.String, Raw = "", Set = set },
            },
        ]);

    // (f) `category In {Laptops}` fires for a categorized transaction and no other.
    [Fact]
    public void Category_in_fires_only_for_that_category()
    {
        var trigger = CategoryIn("Laptops");

        CommissionCalculator.EvaluateTrigger(trigger, Tx("Laptops")).Should().BeTrue();
        CommissionCalculator.EvaluateTrigger(trigger, Tx("Servers")).Should().BeFalse();
        // Uncategorized → the category filter simply does not match (not an error).
        CommissionCalculator.EvaluateTrigger(trigger, Tx(null)).Should().BeFalse();
    }

    // The category field is in the catalog as a String field, so Equal works too, case-insensitively.
    [Fact]
    public void Category_equal_is_case_insensitive()
    {
        var trigger = Trigger.When(LogicalOperator.And,
        [
            new Condition
            {
                Field = "category",
                Operator = ConditionOperator.Equal,
                Value = new ConditionValue { Type = ConditionValueType.String, Raw = "Laptops" },
            },
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, Tx("laptops")).Should().BeTrue();
    }

    // (g) NO REGRESSION: an uncategorized transaction still credits under rules that do not filter on
    // category — a rule with no condition (Always), and a rule filtering on a different field.
    [Fact]
    public void Uncategorized_transaction_still_matches_rules_without_a_category_condition()
    {
        // Rule with no trigger condition → always fires.
        CommissionCalculator.EvaluateTrigger(Trigger.Always(), Tx(null)).Should().BeTrue();

        // Rule filtering on Source (not category) → still fires for an uncategorized CrmSync transaction.
        var sourceTrigger = Trigger.When(LogicalOperator.And,
        [
            new Condition
            {
                Field = "source",
                Operator = ConditionOperator.Equal,
                Value = new ConditionValue { Type = ConditionValueType.String, Raw = "CrmSync" },
            },
        ]);
        CommissionCalculator.EvaluateTrigger(sourceTrigger, Tx(null)).Should().BeTrue();
    }
}
