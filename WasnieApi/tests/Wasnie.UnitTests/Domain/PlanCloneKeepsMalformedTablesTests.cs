using System.Text.Json;
using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Infrastructure.Persistence.Serialization;
using Wasnie.UnitTests.Builders;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// ★★ CLONING DOES NOT VALIDATE, AND THAT IS DELIBERATE — THIS FILE EXISTS SO NOBODY "FIXES" IT.
///
/// <c>Plan.AddRule</c> and <c>Plan.UpdateRule</c> both refuse to touch a non-Draft plan, so the only
/// route to correcting a rule on an ACTIVE plan is <c>CloneAsNewVersion</c> into a fresh Draft. Eight
/// attainment tables in production violate the invariants this work item introduced, and seven of
/// them belong to active plans. If the clone validated, those seven would be frozen in their broken
/// state permanently: the one door out would be locked from the inside by the rule meant to help.
///
/// So validation guards the WRITE of a NEW table, and the clone — which copies a table that already
/// exists, byte for byte — stays open. A future work item that adds a check to
/// <c>CloneAsNewVersion</c> turns these tests red, which is the point.
/// </summary>
public sealed class PlanCloneKeepsMalformedTablesTests
{
    /// <summary>
    /// Builds the exact table the factories now reject — boundaries in currency, last tier bounded —
    /// the way the system itself does for stored rules: through System.Text.Json, which never calls a
    /// factory. This is how a plan loaded from PlanRules arrives in memory.
    /// </summary>
    private static RateTable MalformedTableAsLoadedFromTheDatabase()
    {
        const string storedJson =
            """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":20000,"rate":0.04},{"attainmentFrom":20000,"attainmentTo":50000,"rate":0.06},{"attainmentFrom":50000,"attainmentTo":100000,"rate":0.08}],"splitAtQuota":false}""";

        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());

        return JsonSerializer.Deserialize<RateTable>(storedJson, opts)!;
    }

    private static Plan ActivePlanCarryingAMalformedRule()
    {
        var plan = new PlanBuilder().Build();

        plan.AddRule(
            "Acelerador Hardware Premium",
            sortOrder: 1,
            measurement: new Measurement { Type = MeasurementType.Revenue, SourceField = "amount" },
            rateTable: MalformedTableAsLoadedFromTheDatabase());

        plan.Activate("test-user", DateTimeOffset.UtcNow, Guid.NewGuid());
        return plan;
    }

    [Fact]
    public void A_plan_whose_rule_has_a_malformed_table_can_still_be_cloned()
    {
        var plan = ActivePlanCarryingAMalformedRule();

        var act = () => plan.CloneAsNewVersion("test-user", DateTimeOffset.UtcNow, Guid.NewGuid);

        act.Should().NotThrow(
            "cloning is the only way to repair a rule on an active plan; validating here would lock " +
            "the seven active malformed rules in place forever");
    }

    [Fact]
    public void The_clone_carries_the_malformed_table_across_unchanged()
    {
        var plan = ActivePlanCarryingAMalformedRule();

        var clone = plan.CloneAsNewVersion("test-user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.Version.Should().Be(plan.Version + 1);
        clone.Status.Should().Be(PlanStatus.Draft);
        clone.Rules.Should().HaveCount(1);

        var tiers = clone.Rules[0].RateTable.AttainmentTiers!;
        tiers.Should().HaveCount(3);
        tiers[2].AttainmentTo.Should().Be(
            100000m, "the clone reproduces the table as it stands; correcting it is the editor's job " +
                     "on the resulting Draft, not a silent rewrite");
    }

    [Fact]
    public void The_resulting_draft_refuses_to_save_the_malformed_table_back_through_a_factory()
    {
        // The clone is a Draft, so its rules are editable again — and an edit goes through
        // RateTableRequest.ToDomain, which does validate. That is the intended shape of the repair:
        // the broken table travels into a Draft, and the first deliberate edit has to fix it.
        var clone = ActivePlanCarryingAMalformedRule()
            .CloneAsNewVersion("test-user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.Status.Should().Be(PlanStatus.Draft);

        var stillBroken = clone.Rules[0].RateTable.AttainmentTiers!
            .Select(t => new AttainmentTier
            {
                AttainmentFrom = t.AttainmentFrom,
                AttainmentTo = t.AttainmentTo,
                Rate = t.Rate,
            })
            .ToList();

        FluentActions.Invoking(() => RateTable.AttainmentBased(stillBroken))
            .Should().Throw<Wasnie.Domain.Exceptions.DomainException>()
            .WithMessage("*last tier must be open-ended*");
    }
}
