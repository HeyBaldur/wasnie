using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// The clawback policy is per plan and OPT-IN: every plan that existed before this subsystem has
/// both fields null, which is what keeps clawbacks inert until a tenant configures a maturation
/// window on purpose.
/// </summary>
public sealed class PlanClawbackPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    private static Plan NewPlan() => Plan.Create(
        Guid.NewGuid(), "Plan", "desc",
        DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
        "EUR", "admin", Guid.NewGuid(), Now, Guid.NewGuid());

    private static Plan ActivePlan()
    {
        var plan = NewPlan();
        plan.AddRule("Base", sortOrder: 1,
            measurement: new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            },
            rateTable: RateTable.Flat(0.05m));
        plan.Activate("admin", Now, Guid.NewGuid());
        return plan;
    }

    [Fact]
    public void A_new_plan_has_no_clawback_policy()
    {
        var plan = NewPlan();

        plan.ClawbackMaturationDays.Should().BeNull();
        plan.ClawbackCapPercent.Should().BeNull();
    }

    [Fact]
    public void The_policy_can_be_set_and_cleared()
    {
        var plan = NewPlan();

        plan.SetClawbackPolicy(90, 50m, "admin", Now);
        plan.ClawbackMaturationDays.Should().Be(90);
        plan.ClawbackCapPercent.Should().Be(50m);

        plan.SetClawbackPolicy(null, null, "admin", Now);
        plan.ClawbackMaturationDays.Should().BeNull();
        plan.ClawbackCapPercent.Should().BeNull();
    }

    [Fact]
    public void An_active_plan_can_still_change_its_policy()
    {
        // Deliberate: the policy is not part of the frozen calculation. Every ledger entry stores the
        // MaturationDays it used, so a change moves future clawbacks only.
        var plan = ActivePlan();

        var act = () => plan.SetClawbackPolicy(60, 30m, "admin", Now);

        act.Should().NotThrow();
        plan.Status.Should().Be(PlanStatus.Active);
    }

    [Fact]
    public void An_archived_plan_refuses_a_policy_change()
    {
        var plan = ActivePlan();
        plan.Archive("admin", Now, Guid.NewGuid());

        var act = () => plan.SetClawbackPolicy(60, 30m, "admin", Now);

        act.Should().Throw<DomainException>().WithMessage("*archived*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Maturation_days_must_be_positive(int days)
    {
        var act = () => NewPlan().SetClawbackPolicy(days, 50m, "admin", Now);

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void The_cap_must_be_a_percentage(decimal cap)
    {
        var act = () => NewPlan().SetClawbackPolicy(90, cap, "admin", Now);

        act.Should().Throw<DomainException>().WithMessage("*between 0 and 100*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void The_cap_boundaries_are_valid(decimal cap)
    {
        var act = () => NewPlan().SetClawbackPolicy(90, cap, "admin", Now);

        act.Should().NotThrow();
    }

    // ── The policy survives a new version ────────────────────────────────────
    // A renewal clones the plan. When the clone dropped the policy, the new version looked identical
    // to the one it replaced, was activated as routine housekeeping, and quietly stopped recovering
    // unearned commission — no screen and no audit entry said so. These pin it shut.

    [Fact]
    public void A_new_version_inherits_the_clawback_policy_of_the_one_it_replaces()
    {
        var v1 = ActivePlan();
        v1.SetClawbackPolicy(180, 50m, "admin", Now);

        var v2 = v1.CloneAsNewVersion("admin", Now, Guid.NewGuid);

        v2.Version.Should().Be(v1.Version + 1);
        v2.ClawbackMaturationDays.Should().Be(180);
        v2.ClawbackCapPercent.Should().Be(50m);
    }

    [Fact]
    public void A_new_version_of_a_plan_without_a_policy_does_not_invent_one()
    {
        // The mirror image, and just as important: inheriting must not mean creating. A plan that
        // clawed nothing back keeps clawing nothing back.
        var v1 = ActivePlan();

        var v2 = v1.CloneAsNewVersion("admin", Now, Guid.NewGuid);

        v2.ClawbackMaturationDays.Should().BeNull();
        v2.ClawbackCapPercent.Should().BeNull();
    }

    [Fact]
    public void Turning_the_clawback_off_on_a_new_version_stays_a_deliberate_act()
    {
        // Inheritance is a default, not a lock: the new Draft can still switch it off explicitly,
        // and doing so leaves the ORIGINAL version untouched.
        var v1 = ActivePlan();
        v1.SetClawbackPolicy(180, 50m, "admin", Now);
        var v2 = v1.CloneAsNewVersion("admin", Now, Guid.NewGuid);

        v2.SetClawbackPolicy(null, null, "admin", Now);

        v2.ClawbackMaturationDays.Should().BeNull();
        v1.ClawbackMaturationDays.Should().Be(180, "a version already in force is never edited by its successor");
    }
}
