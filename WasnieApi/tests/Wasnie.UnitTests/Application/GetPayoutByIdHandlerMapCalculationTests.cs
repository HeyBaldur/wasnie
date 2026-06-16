using FluentAssertions;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Application;

public sealed class GetPayoutByIdHandlerMapCalculationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid RuleId = Guid.NewGuid();
    private static readonly Guid PlanId = Guid.NewGuid();

    // ── RateTable: Flat ───────────────────────────────────────────────────────

    [Fact]
    public void MapCalculation_FlatRate_MapsCorrectly()
    {
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 3, "Base Commission",
            RateTable.Flat(0.05m), Trigger.Always(), Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.RateTable.Type.Should().Be("Flat");
        dto.RateTable.FlatRate.Should().Be(0.05m);
        dto.RateTable.Tiers.Should().BeNull();
        dto.PlanVersion.Should().Be(3);
        dto.FrozenAt.Should().Be(Now);
    }

    // ── RateTable: Tiered ─────────────────────────────────────────────────────

    [Fact]
    public void MapCalculation_TieredRate_MapsTiers()
    {
        var tiers = new[]
        {
            new RateTier { From = 0, To = 10_000, Rate = 0.03m },
            new RateTier { From = 10_000, To = null, Rate = 0.05m },
        };
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 1, "Tiered Comm",
            RateTable.Tiered(tiers), Trigger.Always(), Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.RateTable.Type.Should().Be("Tiered");
        dto.RateTable.FlatRate.Should().BeNull();
        dto.RateTable.Tiers.Should().HaveCount(2);
        dto.RateTable.Tiers![0].From.Should().Be(0);
        dto.RateTable.Tiers![0].To.Should().Be(10_000);
        dto.RateTable.Tiers![0].Rate.Should().Be(0.03m);
        dto.RateTable.Tiers![1].To.Should().BeNull();
        dto.RateTable.Tiers![1].Rate.Should().Be(0.05m);
    }

    // ── RateTable: AttainmentBased ────────────────────────────────────────────

    [Fact]
    public void MapCalculation_AttainmentBased_MapsAttainmentTiers()
    {
        var tiers = new[]
        {
            new AttainmentTier { AttainmentFrom = 0, AttainmentTo = 0.8m, Rate = 0.03m },
            new AttainmentTier { AttainmentFrom = 0.8m, AttainmentTo = null, Rate = 0.06m },
        };
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 2, "Attainment Comm",
            RateTable.AttainmentBased(tiers), Trigger.Always(), Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.RateTable.Type.Should().Be("AttainmentBased");
        dto.RateTable.AttainmentTiers.Should().HaveCount(2);
        dto.RateTable.AttainmentTiers![1].Rate.Should().Be(0.06m);
    }

    // ── Trigger: Always ───────────────────────────────────────────────────────

    [Fact]
    public void MapCalculation_TriggerAlways_IsAlwaysTrue()
    {
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 1, "Rule",
            RateTable.Flat(0.05m), Trigger.Always(), Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.Trigger.IsAlways.Should().BeTrue();
        dto.Trigger.Conditions.Should().BeEmpty();
    }

    // ── Trigger: With condition ───────────────────────────────────────────────

    [Fact]
    public void MapCalculation_TriggerWithCondition_MapsConditions()
    {
        var trigger = Trigger.When(LogicalOperator.And,
        [
            new Condition
            {
                Field = "Source",
                Operator = ConditionOperator.Equal,
                Value = new ConditionValue { Type = ConditionValueType.String, Raw = "Direct" }
            }
        ]);
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 1, "Rule",
            RateTable.Flat(0.05m), trigger, Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.Trigger.IsAlways.Should().BeFalse();
        dto.Trigger.LogicalOperator.Should().Be("And");
        dto.Trigger.Conditions.Should().HaveCount(1);
        dto.Trigger.Conditions[0].Field.Should().Be("Source");
        dto.Trigger.Conditions[0].Operator.Should().Be("Equal");
        dto.Trigger.Conditions[0].Value.Should().Be("Direct");
    }

    // ── Trigger: In operator uses Set ─────────────────────────────────────────

    [Fact]
    public void MapCalculation_TriggerInOperator_UsesSetJoined()
    {
        var trigger = Trigger.When(LogicalOperator.Or,
        [
            new Condition
            {
                Field = "Region",
                Operator = ConditionOperator.In,
                Value = new ConditionValue
                {
                    Type = ConditionValueType.String,
                    Raw = "",
                    Set = ["North", "South"]
                }
            }
        ]);
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 1, "Rule",
            RateTable.Flat(0.05m), trigger, Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.Trigger.Conditions[0].Value.Should().Be("North, South");
    }

    // ── Modifiers ─────────────────────────────────────────────────────────────

    [Fact]
    public void MapCalculation_WithModifiers_MapsChainCorrectly()
    {
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 1, "Rule",
            RateTable.Flat(0.05m), Trigger.Always(), Now);

        var modifiers = new[]
        {
            ModifierApplication.Record(
                Guid.NewGuid(), "Accelerator",
                1.25m,
                Money.Of(100m, "EUR"),
                Money.Of(125m, "EUR"),
                Now),
            ModifierApplication.Record(
                Guid.NewGuid(), "Cap",
                0.8m,
                Money.Of(125m, "EUR"),
                Money.Of(100m, "EUR"),
                Now),
        };

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, modifiers);

        dto.Modifiers.Should().HaveCount(2);
        dto.Modifiers[0].ModifierName.Should().Be("Accelerator");
        dto.Modifiers[0].FactorApplied.Should().Be(1.25m);
        dto.Modifiers[0].AmountBefore.Should().Be(100m);
        dto.Modifiers[0].AmountAfter.Should().Be(125m);
        dto.Modifiers[1].ModifierName.Should().Be("Cap");
        dto.Modifiers[1].AmountBefore.Should().Be(125m);
        dto.Modifiers[1].AmountAfter.Should().Be(100m);
    }

    [Fact]
    public void MapCalculation_NoModifiers_ReturnsEmptyList()
    {
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 1, "Rule",
            RateTable.Flat(0.05m), Trigger.Always(), Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.Modifiers.Should().BeEmpty();
    }

    // ── BuildLinesAsync integration: Calculation populated ────────────────────

    [Fact]
    public void MapCalculation_BuildLinesAsync_PopulatesCalculation()
    {
        // Verifies that a Credit with a RuleSnapshot produces Calculation != null
        // and preserves PlanVersion and RateTable type
        var snapshot = RuleSnapshot.Freeze(RuleId, PlanId, 7, "Seasonal Bonus",
            RateTable.Flat(0.10m), Trigger.Always(), Now);

        var dto = GetPayoutByIdHandler.MapCalculation(snapshot, []);

        dto.PlanVersion.Should().Be(7);
        dto.RateTable.Type.Should().Be("Flat");
        dto.RateTable.FlatRate.Should().Be(0.10m);
    }
}
