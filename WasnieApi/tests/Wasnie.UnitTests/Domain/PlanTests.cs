using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Events;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.UnitTests.Builders;

namespace Wasnie.UnitTests.Domain;

public sealed class PlanTests
{
    private static Measurement DefaultMeasurement => new()
    {
        Type = MeasurementType.Revenue,
        SourceField = "amount",
        Aggregation = MeasurementAggregation.Sum
    };

    // ── Creation ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsInitialState()
    {
        var plan = new PlanBuilder().Build();

        plan.Status.Should().Be(PlanStatus.Draft);
        plan.Version.Should().Be(1);
        plan.Rules.Should().BeEmpty();
        plan.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_RaisesPlanCreatedEvent()
    {
        var plan = new PlanBuilder().Build();
        plan.DomainEvents.Should().ContainSingle(e => e is PlanCreatedEvent);
    }

    // ── AddRule ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddRule_OnDraft_AddsRuleSuccessfully()
    {
        var plan = new PlanBuilder().Build();
        plan.AddRule("Commission", 1, DefaultMeasurement, RateTable.Flat(0.05m));
        plan.Rules.Should().ContainSingle(r => r.Name == "Commission" && r.IsActive);
    }

    [Fact]
    public void AddRule_OnActive_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var act = () => plan.AddRule("New Rule", 2, DefaultMeasurement, RateTable.Flat(0.1m));
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    [Fact]
    public void AddRule_OnArchived_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var act = () => plan.AddRule("New Rule", 2, DefaultMeasurement, RateTable.Flat(0.1m));
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    // ── RemoveRule ────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveRule_OnDraft_DeactivatesRule()
    {
        var plan = new PlanBuilder().Build();
        var rule = plan.AddRule("Commission", 1, DefaultMeasurement, RateTable.Flat(0.05m));

        plan.RemoveRule(rule.Id);

        plan.Rules.Should().ContainSingle(r => r.Id == rule.Id && !r.IsActive);
    }

    [Fact]
    public void RemoveRule_OnActive_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        var ruleId = plan.Rules[0].Id;
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var act = () => plan.RemoveRule(ruleId);
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    [Fact]
    public void RemoveRule_UnknownId_ThrowsDomainException()
    {
        var plan = new PlanBuilder().Build();
        var act = () => plan.RemoveRule(Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*not found*");
    }

    // ── UpdateRule ────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateRule_OnDraft_UpdatesRuleProperties()
    {
        var plan = new PlanBuilder().Build();
        var rule = plan.AddRule("Old Name", 1, DefaultMeasurement, RateTable.Flat(0.05m));

        plan.UpdateRule(rule.Id, "New Name", 2, DefaultMeasurement, RateTable.Flat(0.1m));

        var updated = plan.Rules.Single(r => r.Id == rule.Id);
        updated.Name.Should().Be("New Name");
        updated.SortOrder.Should().Be(2);
    }

    [Fact]
    public void UpdateRule_OnActive_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        var ruleId = plan.Rules[0].Id;
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var act = () => plan.UpdateRule(ruleId, "New Name", 1, DefaultMeasurement, RateTable.Flat(0.1m));
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    // ── Activate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Activate_DraftWithActiveRule_Succeeds()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.Status.Should().Be(PlanStatus.Active);
    }

    [Fact]
    public void Activate_DraftWithNoRules_ThrowsDomainException()
    {
        var plan = new PlanBuilder().Build();
        var act = () => plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*at least one active rule*");
    }

    [Fact]
    public void Activate_DraftWithAllRulesRemoved_ThrowsDomainException()
    {
        var plan = new PlanBuilder().Build();
        var rule = plan.AddRule("Commission", 1, DefaultMeasurement, RateTable.Flat(0.05m));
        plan.RemoveRule(rule.Id);

        var act = () => plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*at least one active rule*");
    }

    [Fact]
    public void Activate_AlreadyActive_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var act = () => plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    [Fact]
    public void Activate_Archived_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var act = () => plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    [Fact]
    public void Activate_RaisesPlanActivatedEvent()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.ClearDomainEvents();

        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        plan.DomainEvents.Should().ContainSingle(e => e is PlanActivatedEvent);
    }

    // ── Archive ───────────────────────────────────────────────────────────────

    [Fact]
    public void Archive_ActivePlan_SetsArchivedStatus()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.Status.Should().Be(PlanStatus.Archived);
    }

    [Fact]
    public void Archive_AlreadyArchived_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var act = () => plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Active*");
    }

    [Fact]
    public void Archive_DraftPlan_ThrowsDomainException()
    {
        var plan = new PlanBuilder().Build();
        var act = () => plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("*Active*");
    }

    [Fact]
    public void Archive_RaisesPlanArchivedEvent()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.ClearDomainEvents();

        plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        plan.DomainEvents.Should().ContainSingle(e => e is PlanArchivedEvent);
    }

    // ── CloneAsNewVersion ────────────────────────────────────────────────────

    [Fact]
    public void CloneAsNewVersion_CopiesPropertiesAndIncrementsVersion()
    {
        var plan = new PlanBuilder().WithName("Sales Plan").BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var clone = plan.CloneAsNewVersion("user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.Name.Should().Be("Sales Plan");
        clone.Version.Should().Be(2);
        clone.Status.Should().Be(PlanStatus.Draft);
        clone.TenantId.Should().Be(plan.TenantId);
        clone.Id.Should().NotBe(plan.Id);
    }

    [Fact]
    public void CloneAsNewVersion_CopiesActiveRulesOnly()
    {
        var plan = new PlanBuilder().Build();
        var kept = plan.AddRule("Kept", 1, DefaultMeasurement, RateTable.Flat(0.05m));
        var removed = plan.AddRule("Removed", 2, DefaultMeasurement, RateTable.Flat(0.1m));
        plan.RemoveRule(removed.Id);
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var clone = plan.CloneAsNewVersion("user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.Rules.Should().ContainSingle(r => r.Name == "Kept" && r.IsActive);
        clone.Rules.Should().NotContain(r => r.Name == "Removed");
    }

    [Fact]
    public void CloneAsNewVersion_ClonesGetNewRuleIds()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        var originalRuleId = plan.Rules[0].Id;

        var clone = plan.CloneAsNewVersion("user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.Rules[0].Id.Should().NotBe(originalRuleId);
    }

    [Fact]
    public void CloneAsNewVersion_RaisesPlanVersionClonedEvent()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.ClearDomainEvents();

        var clone = plan.CloneAsNewVersion("user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.DomainEvents.Should().ContainSingle(e => e is PlanVersionClonedEvent);
    }

    [Fact]
    public void CloneAsNewVersion_FromDraft_ThrowsDomainException()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        var act = () => plan.CloneAsNewVersion("user", DateTimeOffset.UtcNow, Guid.NewGuid);
        act.Should().Throw<DomainException>().WithMessage("*Draft*");
    }

    [Fact]
    public void CloneAsNewVersion_FromArchived_Succeeds()
    {
        var plan = new PlanBuilder().WithName("Sales Plan").BuildWithOneRule();
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());
        plan.Archive("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var clone = plan.CloneAsNewVersion("user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.Status.Should().Be(PlanStatus.Draft);
        clone.Version.Should().Be(2);
    }

    // ── RateTable ────────────────────────────────────────────────────────────

    [Fact]
    public void RateTable_TieredWithNoTiers_ThrowsDomainException()
    {
        var act = () => RateTable.Tiered([]);
        act.Should().Throw<DomainException>().WithMessage("*at least one tier*");
    }

    [Fact]
    public void RateTable_TieredWithOverlappingRanges_ThrowsDomainException()
    {
        var tiers = new List<RateTier>
        {
            new() { From = 0m, To = 100m, Rate = 0.05m },
            new() { From = 80m, To = null, Rate = 0.08m }  // overlaps
        };

        var act = () => RateTable.Tiered(tiers);
        act.Should().Throw<DomainException>().WithMessage("*non-overlapping*");
    }

    [Fact]
    public void RateTable_TieredNonLastWithNullTo_ThrowsDomainException()
    {
        var tiers = new List<RateTier>
        {
            new() { From = 0m, To = null, Rate = 0.05m }, // null To on non-last
            new() { From = 100m, To = null, Rate = 0.08m }
        };

        var act = () => RateTable.Tiered(tiers);
        act.Should().Throw<DomainException>().WithMessage("*upper bound*");
    }

    [Fact]
    public void RateTable_ValidTiered_CreatesInstance()
    {
        var tiers = new List<RateTier>
        {
            new() { From = 0m, To = 100m, Rate = 0.05m },
            new() { From = 101m, To = null, Rate = 0.08m }
        };

        var table = RateTable.Tiered(tiers);
        table.Type.Should().Be(RateTableType.Tiered);
        table.Tiers.Should().HaveCount(2);
    }

    // ── WI-CALC-A.0: PeriodType + Rule.Tag + Rule.EffectivePeriod ────────────

    [Fact]
    public void Create_WithPeriodType_SetsPeriodType()
    {
        var plan = Plan.Create(
            Guid.NewGuid(), "Test Plan", "desc",
            DateRange.Of(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            "EUR", "user", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(),
            periodType: PlanPeriodType.Monthly);

        plan.PeriodType.Should().Be(PlanPeriodType.Monthly);
    }

    [Fact]
    public void Create_WithDefaultPeriodType_PeriodTypeIsNull()
    {
        var plan = new PlanBuilder().Build();

        plan.PeriodType.Should().BeNull();
    }

    [Fact]
    public void CloneAsNewVersion_CopiesPeriodType()
    {
        var plan = Plan.Create(
            Guid.NewGuid(), "Test Plan", "desc",
            DateRange.Of(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            "EUR", "user", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(),
            periodType: PlanPeriodType.Quarterly);
        plan.AddRule("Rule", 1, DefaultMeasurement, RateTable.Flat(0.05m));
        plan.Activate("user", DateTimeOffset.UtcNow, Guid.NewGuid());

        var clone = plan.CloneAsNewVersion("user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.PeriodType.Should().Be(PlanPeriodType.Quarterly);
    }

    [Fact]
    public void AddRule_WithTagAndEffectivePeriod_RuleHasCorrectValues()
    {
        var plan = new PlanBuilder().Build();
        var start = new DateOnly(2025, 4, 1);
        var end = new DateOnly(2025, 6, 30);
        var period = DateRange.Of(start, end);

        var rule = plan.AddRule("Promo", 1, DefaultMeasurement, RateTable.Flat(0.1m),
            effectivePeriod: period, tag: "Promo Q2");

        rule.Tag.Should().Be("Promo Q2");
        rule.EffectivePeriod.Should().NotBeNull();
        rule.EffectivePeriod!.Start.Should().Be(start);
        rule.EffectivePeriod.End.Should().Be(end);
    }

    [Fact]
    public void AddRule_WithTagExceeding50Characters_ThrowsDomainException()
    {
        var plan = new PlanBuilder().Build();
        var longTag = new string('x', 51);

        var act = () => plan.AddRule("Rule", 1, DefaultMeasurement, RateTable.Flat(0.05m), tag: longTag);

        act.Should().Throw<DomainException>().WithMessage("*50 characters*");
    }

    [Fact]
    public void AddRule_WithNullTag_RuleTagIsNull()
    {
        var plan = new PlanBuilder().Build();

        var rule = plan.AddRule("Rule", 1, DefaultMeasurement, RateTable.Flat(0.05m), tag: null);

        rule.Tag.Should().BeNull();
    }

    // ── Units measurement validation (WI-UNITS-MEASUREMENT) ──────────────────

    [Fact]
    public void AddRule_UnitsWithFlat_Succeeds()
    {
        var plan = new PlanBuilder().Build();
        var unitsMeasurement = new Measurement
        {
            Type = MeasurementType.Units,
            SourceField = "amount",
            Aggregation = MeasurementAggregation.Sum,
        };

        var rule = plan.AddRule("Units Commission", 1, unitsMeasurement, RateTable.Flat(2.00m));

        rule.Measurement.Type.Should().Be(MeasurementType.Units);
        rule.RateTable.FlatRate.Should().Be(2.00m);
    }

    [Fact]
    public void AddRule_UnitsWithTiered_ThrowsDomainException()
    {
        var plan = new PlanBuilder().Build();
        var unitsMeasurement = new Measurement
        {
            Type = MeasurementType.Units,
            SourceField = "amount",
            Aggregation = MeasurementAggregation.Sum,
        };
        var tieredTable = RateTable.Tiered([
            new RateTier { From = 0, To = 100, Rate = 0.05m },
            new RateTier { From = 100, To = null, Rate = 0.10m },
        ]);

        Action act = () => plan.AddRule("Units Tiered", 1, unitsMeasurement, tieredTable);

        act.Should().Throw<DomainException>()
            .WithMessage("*Units measurement only supports a Flat rate table*");
    }

    [Fact]
    public void AddRule_UnitsWithAttainment_ThrowsDomainException()
    {
        var plan = new PlanBuilder().Build();
        var unitsMeasurement = new Measurement
        {
            Type = MeasurementType.Units,
            SourceField = "amount",
            Aggregation = MeasurementAggregation.Sum,
        };
        var attainmentTable = RateTable.AttainmentBased([
            new AttainmentTier { AttainmentFrom = 0m, AttainmentTo = 1.0m, Rate = 0.05m },
        ]);

        Action act = () => plan.AddRule("Units Attainment", 1, unitsMeasurement, attainmentTable);

        act.Should().Throw<DomainException>()
            .WithMessage("*Units measurement only supports a Flat rate table*");
    }

    [Fact]
    public void AddRule_RevenueWithTiered_Succeeds()
    {
        // Regression: Revenue + Tiered must still be allowed after the Units guard was added.
        var plan = new PlanBuilder().Build();
        var tieredTable = RateTable.Tiered([
            new RateTier { From = 0, To = 100, Rate = 0.05m },
            new RateTier { From = 100, To = null, Rate = 0.10m },
        ]);

        var rule = plan.AddRule("Rev Tiered", 1, DefaultMeasurement, tieredTable);

        rule.RateTable.Type.Should().Be(RateTableType.Tiered);
    }
}
