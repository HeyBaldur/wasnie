using System.Text.Json;
using FluentAssertions;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence.Serialization;
using Wasnie.UnitTests.Builders;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// The seventh invariant: a rate is a FRACTION of its base, and 4 is not 4%.
///
/// ★ THE CASE THIS PINS IS A REAL ONE. On 2026-09-01 an attainment rule was saved with rates of 4
/// and 7 — the per cents typed as whole numbers — and the engine paid a 50,000 base as a credit of
/// 200,000, marking the step `Applied`. All six ladder invariants passed, because all six read
/// bounds and none reads the rate.
///
/// ★ EVERY CASE ASSERTS A CODE, NOT A SENTENCE, for the reason RateTableInvariantTests spells out:
/// the wording lives in the front end's three translation files and may be rewritten there freely.
/// The code and its parameters are the contract.
/// </summary>
public sealed class RateMagnitudeTests
{
    private static RateTier T(decimal from, decimal? to, decimal rate) => new() { From = from, To = to, Rate = rate };

    private static AttainmentTier A(decimal from, decimal? to, decimal rate) =>
        new() { AttainmentFrom = from, AttainmentTo = to, Rate = rate };

    private static DomainCodedException Refusal(Action act)
        => FluentActions.Invoking(act).Should().Throw<DomainCodedException>().Which;

    private static Measurement Revenue() =>
        new() { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum };

    private static Measurement Units() =>
        new() { Type = MeasurementType.Units, SourceField = "quantity", Aggregation = MeasurementAggregation.Sum };

    // ── Attainment — the shape of the reproduced defect ───────────────────────────────────────

    /// <summary>
    /// The exact table from the incident: a well-formed ladder (0–1, 1–open) whose rates are 4 and 7.
    /// Nothing about its SHAPE is wrong, which is why the six invariants let it through.
    /// </summary>
    [Fact]
    public void Attainment_rejects_a_rate_of_4_where_0_point_04_was_meant()
    {
        var refusal = Refusal(() => RateTable.AttainmentBased([A(0m, 1m, 4m), A(1m, null, 7m)]));

        refusal.Code.Should().Be(RateTableInvariant.RateAboveMaximum);
        refusal.Parameters["tierNumber"].Should().Be(1);
        refusal.Parameters["rate"].Should().Be(4m);
        refusal.Parameters["maximum"].Should().Be(RateMagnitude.MaxFractionalRate);
    }

    [Fact]
    public void Attainment_accepts_the_same_ladder_written_as_fractions()
    {
        var table = RateTable.AttainmentBased([A(0m, 1m, 0.04m), A(1m, null, 0.07m)]);

        table.AttainmentTiers.Should().HaveCount(2);
        table.AttainmentTiers![0].Rate.Should().Be(0.04m);
        table.AttainmentTiers![1].Rate.Should().Be(0.07m);
    }

    /// <summary>The refusal points at the tier that is wrong, not at the first one.</summary>
    [Fact]
    public void Attainment_names_the_offending_tier_when_it_is_not_the_first()
    {
        var refusal = Refusal(() => RateTable.AttainmentBased([A(0m, 1m, 0.04m), A(1m, null, 7m)]));

        refusal.Code.Should().Be(RateTableInvariant.RateAboveMaximum);
        refusal.Parameters["tierNumber"].Should().Be(2);
        refusal.Parameters["rate"].Should().Be(7m);
    }

    // ── Tiered — the same rule, the other ladder ──────────────────────────────────────────────

    [Fact]
    public void Tiered_rejects_a_rate_above_the_maximum()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(0m, 1000m, 0.05m), T(1000m, null, 8m)]));

        refusal.Code.Should().Be(RateTableInvariant.RateAboveMaximum);
        refusal.Parameters["tierNumber"].Should().Be(2);
        refusal.Parameters["rate"].Should().Be(8m);
    }

    [Fact]
    public void Tiered_accepts_the_highest_rate_that_exists_in_production()
    {
        // 0.15 is the largest tiered rate in PlanRules. Nothing real is refused by this ceiling.
        var table = RateTable.Tiered([T(0m, 1000m, 0.075m), T(1000m, null, 0.15m)]);

        table.Tiers![1].Rate.Should().Be(0.15m);
    }

    [Fact]
    public void A_negative_tier_rate_is_a_different_refusal_from_one_that_is_too_high()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(0m, null, -0.05m)]));

        refusal.Code.Should().Be(RateTableInvariant.RateBelowZero);
        refusal.Parameters["tierNumber"].Should().Be(1);
        refusal.Parameters["rate"].Should().Be(-0.05m);
    }

    // ── The boundary ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ STRICTLY GREATER, AND THE REASON IS A REAL ROW. The flat rule "Full payout" is exactly 1.00
    /// — a referral that pays the whole sale. A ceiling written as `>=` would refuse it. 100% is
    /// unusual; 400% is a typo, and only the second is what this guard is for.
    /// </summary>
    [Fact]
    public void A_rate_of_exactly_one_is_allowed()
    {
        var table = RateTable.AttainmentBased([A(0m, 1m, 0.5m), A(1m, null, RateMagnitude.MaxFractionalRate)]);

        table.AttainmentTiers![1].Rate.Should().Be(1m);
    }

    [Fact]
    public void A_rate_of_zero_is_allowed()
        => RateTable.Tiered([T(0m, 1000m, 0m), T(1000m, null, 0.05m)]).Tiers![0].Rate.Should().Be(0m);

    // ── The shape checks still speak first ────────────────────────────────────────────────────

    /// <summary>
    /// A table typed wrong in both ways at once — money in an attainment ladder AND per cents as
    /// whole numbers — is refused for its BOUNDS, because the rate check runs last. Pinned so the
    /// ordering decision documented in ValidateLadder's remarks is a fact and not a comment.
    /// </summary>
    [Fact]
    public void A_ladder_broken_in_shape_and_in_rate_is_refused_for_its_shape_first()
    {
        var refusal = Refusal(() => RateTable.AttainmentBased([A(0m, 20000m, 4m), A(20000m, 50000m, 6m)]));

        refusal.Code.Should().Be(RateTableInvariant.LastTierMustBeOpen);
    }

    // ── The HTTP door ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_command_door_refuses_an_attainment_rate_of_4()
    {
        var request = new RateTableRequest(
            RateTableType.AttainmentBased,
            FlatRate: null,
            Tiers: null,
            AttainmentTiers: [new AttainmentTierRequest(0m, 1m, 4m), new AttainmentTierRequest(1m, null, 7m)]);

        Refusal(() => request.ToDomain()).Code.Should().Be(RateTableInvariant.RateAboveMaximum);
    }

    // ── Flat, where the rate is not always a fraction ─────────────────────────────────────────

    [Fact]
    public void A_flat_revenue_rule_refuses_a_rate_above_the_maximum()
    {
        var plan = new PlanBuilder().Build();

        var refusal = Refusal(() => plan.AddRule(
            "Comisión base",
            sortOrder: 1,
            measurement: Revenue(),
            rateTable: RateTable.Flat(4m)));

        refusal.Code.Should().Be(RateTableInvariant.RateAboveMaximum);
        refusal.Parameters["rate"].Should().Be(4m);
        refusal.Parameters.Should().NotContainKey(
            "tierNumber",
            "a flat table has no tiers: the client picks its wording by the ABSENCE of this key, and " +
            "it interpolates every parameter it is sent — a null would print the word \"null\"");
    }

    /// <summary>
    /// ★★ THE CARVE-OUT, PINNED. Four rules in production are Units spiffs paying €3 or €5 A UNIT.
    /// A blanket ceiling on RateTable.Flat would have refused every one of them, and a validation
    /// that blocks a correct configuration is worse than the one it replaced.
    /// </summary>
    [Fact]
    public void A_flat_units_rule_may_pay_five_euros_a_unit()
    {
        var plan = new PlanBuilder().Build();

        var act = () => plan.AddRule(
            "Spiff por Volumen de Unidades",
            sortOrder: 1,
            measurement: Units(),
            rateTable: RateTable.Flat(5m));

        act.Should().NotThrow("in Units mode the flat rate is currency per unit, not a share of anything");
        plan.Rules.Should().HaveCount(1);
    }

    [Fact]
    public void A_flat_revenue_rule_of_exactly_one_is_allowed()
    {
        var plan = new PlanBuilder().Build();

        plan.AddRule("Full payout", sortOrder: 1, measurement: Revenue(), rateTable: RateTable.Flat(1m));

        plan.Rules[0].RateTable.FlatRate.Should().Be(1m);
    }

    [Fact]
    public void Editing_a_rule_refuses_a_flat_rate_above_the_maximum_too()
    {
        var plan = new PlanBuilder().BuildWithOneRule();
        var ruleId = plan.Rules[0].Id;

        var refusal = Refusal(() => plan.UpdateRule(
            ruleId,
            "Base Commission",
            sortOrder: 1,
            measurement: Revenue(),
            rateTable: RateTable.Flat(50m)));

        refusal.Code.Should().Be(RateTableInvariant.RateAboveMaximum);
    }

    // ── §D4 — the emergency exit stays open ───────────────────────────────────────────────────

    /// <summary>
    /// ★★ CLONING A PLAN WHOSE RULE HAS A 400% RATE MUST STILL WORK, AND THIS IS THE TEST THAT SAYS
    /// SO. Rule E2345397 is in the database right now with rates of 4 and 7. AddRule and UpdateRule
    /// both demand Draft, so cloning into a new version is the ONLY way anyone can correct it. Put
    /// this check in Rule.Create — the constructor the clone shares — and the one door out is locked
    /// from the inside by the rule meant to help (§D4). A future change that validates in the clone
    /// turns this red, which is the point.
    /// </summary>
    [Fact]
    public void A_plan_whose_rule_has_a_400_percent_rate_can_still_be_cloned()
    {
        var plan = ActivePlanCarryingA400PercentRate();

        var act = () => plan.CloneAsNewVersion("test-user", DateTimeOffset.UtcNow, Guid.NewGuid);

        act.Should().NotThrow(
            "cloning into a Draft is the only route to repairing a rule on an active plan; " +
            "validating here would freeze the 400% rate in production forever");
    }

    [Fact]
    public void The_clone_carries_the_out_of_range_rate_across_unchanged()
    {
        var clone = ActivePlanCarryingA400PercentRate()
            .CloneAsNewVersion("test-user", DateTimeOffset.UtcNow, Guid.NewGuid);

        clone.Status.Should().Be(PlanStatus.Draft);
        clone.Rules[0].RateTable.AttainmentTiers![0].Rate.Should().Be(
            4m, "the clone reproduces the table as it stands; correcting it is the editor's job on " +
                "the resulting Draft, not a silent rewrite");
    }

    [Fact]
    public void The_resulting_draft_refuses_to_save_the_out_of_range_rate_back_through_a_factory()
    {
        // The intended shape of the repair: the broken table travels into a Draft untouched, and the
        // first deliberate edit has to fix it.
        var clone = ActivePlanCarryingA400PercentRate()
            .CloneAsNewVersion("test-user", DateTimeOffset.UtcNow, Guid.NewGuid);

        var stillBroken = clone.Rules[0].RateTable.AttainmentTiers!
            .Select(t => new AttainmentTier
            {
                AttainmentFrom = t.AttainmentFrom,
                AttainmentTo = t.AttainmentTo,
                Rate = t.Rate,
            })
            .ToList();

        Refusal(() => RateTable.AttainmentBased(stillBroken))
            .Code.Should().Be(RateTableInvariant.RateAboveMaximum);
    }

    /// <summary>
    /// Rule E2345397's table, arriving the way a stored rule actually arrives — through
    /// System.Text.Json, which never calls a factory. The ladder itself is well formed: 0–1 then
    /// 1–open. Only the rates are wrong.
    /// </summary>
    private static RateTable TableAsLoadedFromTheDatabase()
    {
        const string storedJson =
            """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":1,"rate":4},{"attainmentFrom":1,"attainmentTo":null,"rate":7}],"splitAtQuota":false}""";

        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());

        return JsonSerializer.Deserialize<RateTable>(storedJson, opts)!;
    }

    private static Plan ActivePlanCarryingA400PercentRate()
    {
        var plan = new PlanBuilder().Build();

        plan.AddRule(
            "KAN-27 - Test Num. 2",
            sortOrder: 1,
            measurement: Revenue(),
            rateTable: TableAsLoadedFromTheDatabase());

        plan.Activate("test-user", DateTimeOffset.UtcNow, Guid.NewGuid());
        return plan;
    }
}
