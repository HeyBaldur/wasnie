using FluentAssertions;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// KAN-83 — the rule that turns purchases, closed periods and this period's usage into a balance.
///
/// ★ THESE ARE MONEY TESTS. A tenant's boost is something they PAID for: counting it twice gives away tokens, losing it
/// takes their money. Every case below is a way the walk could get that wrong.
///
/// ★ DRIVEN THROUGH THE PURE FUNCTION ON PURPOSE. The refusal, the meter and the checkout all read this one result, so a
/// test here covers all three — and unlike a test through the database it cannot pass down a path production never takes.
/// </summary>
public sealed class AssistantBoostLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static BoostLot Lot(long tokens, int boughtDaysAgo, int expiresInDays) =>
        new(tokens, Now.AddDays(-boughtDaysAgo), Now.AddDays(expiresInDays));

    [Fact]
    public void Included_is_spent_before_any_boost()
    {
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 3_000_000,
            usedInPeriod: 1_000_000,
            lots: [Lot(6_000_000, boughtDaysAgo: 10, expiresInDays: 355)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.IncludedRemaining.Should().Be(2_000_000);
        balance.BoostRemaining.Should().Be(6_000_000, "the boost is untouched while the monthly allowance lasts");
        balance.TotalRemaining.Should().Be(8_000_000);
        balance.Exhausted.Should().BeFalse();
    }

    [Fact]
    public void Usage_past_the_allowance_comes_out_of_the_boost()
    {
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 3_000_000,
            usedInPeriod: 4_000_000,
            lots: [Lot(6_000_000, 10, 355)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.IncludedRemaining.Should().Be(0);
        balance.BoostRemaining.Should().Be(5_000_000, "1M of the 4M spent went past the 3M allowance");
        balance.Exhausted.Should().BeFalse();
    }

    [Fact]
    public void Exactly_at_the_allowance_spends_no_boost()
    {
        var balance = AssistantBoostLedger.Compute(3_000_000, 3_000_000, [Lot(3_000_000, 1, 364)], 0, Now);

        balance.IncludedRemaining.Should().Be(0);
        balance.BoostRemaining.Should().Be(3_000_000, "the boundary belongs to the allowance, not the boost");
    }

    [Fact]
    public void A_tenant_with_no_boost_runs_out_at_the_allowance()
    {
        var balance = AssistantBoostLedger.Compute(3_000_000, 3_000_000, [], 0, Now);

        balance.TotalRemaining.Should().Be(0);
        balance.Exhausted.Should().BeTrue();
    }

    [Fact]
    public void Closed_periods_keep_charging_the_boost_after_the_allowance_resets()
    {
        // A fresh period: nothing spent yet, so the allowance is whole again — but last month's overage already ate
        // into the boost and must not come back.
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 3_000_000,
            usedInPeriod: 0,
            lots: [Lot(6_000_000, 40, 325)],
            boostAlreadyCharged: 2_000_000,
            now: Now);

        balance.IncludedRemaining.Should().Be(3_000_000, "the included allowance resets every period");
        balance.BoostRemaining.Should().Be(4_000_000, "the boost does not reset — it is what the tenant paid for");
    }

    [Fact]
    public void The_oldest_lot_pays_first()
    {
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 0,
            usedInPeriod: 3_000_000,
            lots: [Lot(3_000_000, boughtDaysAgo: 300, expiresInDays: 65), Lot(6_000_000, boughtDaysAgo: 5, expiresInDays: 360)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.BoostRemaining.Should().Be(6_000_000, "the older lot was consumed whole, the newer one is intact");
        balance.BoostNextExpiry.Should().Be(Now.AddDays(360), "the only surviving lot is the newer one");
        balance.BoostExpired.Should().Be(0);
    }

    [Fact]
    public void An_expired_lot_loses_what_was_left_of_it_and_the_loss_is_visible()
    {
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 0,
            usedInPeriod: 1_000_000,
            lots: [Lot(3_000_000, boughtDaysAgo: 400, expiresInDays: -35)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.BoostRemaining.Should().Be(0);
        balance.BoostExpired.Should().Be(2_000_000, "1M of the 3M lot was spent before it died");
        balance.BoostNextExpiry.Should().BeNull();
        balance.Exhausted.Should().BeTrue();
    }

    [Fact]
    public void A_fully_spent_expired_lot_reports_no_loss()
    {
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 0,
            usedInPeriod: 3_000_000,
            lots: [Lot(3_000_000, 400, -35)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.BoostExpired.Should().Be(0, "nothing was left to lose — it was all used in time");
    }

    [Fact]
    public void Consumption_is_charged_to_the_dying_lot_before_the_live_one()
    {
        // ★ The usage rows say how much boost was spent, never which lot paid. Charging the lot that is about to expire
        // first is what stops the walk from manufacturing an expiry loss the customer never had to take.
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 0,
            usedInPeriod: 3_000_000,
            lots: [Lot(3_000_000, boughtDaysAgo: 400, expiresInDays: -1), Lot(3_000_000, boughtDaysAgo: 5, expiresInDays: 360)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.BoostExpired.Should().Be(0, "the spend covered the dying lot exactly");
        balance.BoostRemaining.Should().Be(3_000_000, "the live lot survives whole");
    }

    [Fact]
    public void Overshooting_both_the_allowance_and_the_boost_reads_zero_never_negative()
    {
        // A turn that starts inside the balance can end outside it: the check runs before the turn and the turn's size
        // is only known after. The overshoot must not become a debt the next period inherits.
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 3_000_000,
            usedInPeriod: 9_000_000,
            lots: [Lot(1_000_000, 10, 355)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.BoostRemaining.Should().Be(0);
        balance.TotalRemaining.Should().Be(0);
        balance.Exhausted.Should().BeTrue();
    }

    [Fact]
    public void The_next_expiry_is_the_soonest_surviving_lot()
    {
        var balance = AssistantBoostLedger.Compute(
            includedLimit: 0,
            usedInPeriod: 0,
            lots: [Lot(3_000_000, 5, 360), Lot(3_000_000, 200, 165)],
            boostAlreadyCharged: 0,
            now: Now);

        balance.BoostRemaining.Should().Be(6_000_000);
        balance.BoostNextExpiry.Should().Be(Now.AddDays(165));
    }

    [Fact]
    public void Lots_are_walked_by_purchase_date_whatever_order_they_arrive_in()
    {
        var newest = Lot(3_000_000, boughtDaysAgo: 1, expiresInDays: 364);
        var oldest = Lot(3_000_000, boughtDaysAgo: 300, expiresInDays: 65);

        var asGiven = AssistantBoostLedger.Compute(0, 3_000_000, [newest, oldest], 0, Now);
        var reversed = AssistantBoostLedger.Compute(0, 3_000_000, [oldest, newest], 0, Now);

        asGiven.Should().Be(reversed, "the walk orders the lots itself — the query's order must not change the balance");
        asGiven.BoostNextExpiry.Should().Be(Now.AddDays(364));
    }
}

/// <summary>KAN-83 — the invariants of a purchased lot and of a closed period's charge.</summary>
public sealed class AssistantBoostEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static AssistantTokenBoost Boost(long tokens, DateTimeOffset? expiresAt = null) =>
        AssistantTokenBoost.Create(
            Guid.NewGuid(), Guid.NewGuid(), "evt_1", "prod_1", "cs_1",
            tokens, Now, expiresAt ?? Now.AddDays(365));

    [Fact]
    public void A_boost_must_grant_something()
    {
        var zero = () => Boost(0);
        zero.Should().Throw<DomainException>("crediting nothing would record a purchase that gave the customer nothing");

        var negative = () => Boost(-1);
        negative.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_boost_must_outlive_its_purchase()
    {
        var sameInstant = () => Boost(3_000_000, expiresAt: Now);
        sameInstant.Should().Throw<DomainException>("a lot that expires when it is bought was never sellable");
    }

    [Fact]
    public void A_boost_needs_the_stripe_event_that_paid_for_it()
    {
        var noEvent = () => AssistantTokenBoost.Create(
            Guid.NewGuid(), Guid.NewGuid(), "  ", "prod_1", "cs_1", 3_000_000, Now, Now.AddDays(365));

        noEvent.Should().Throw<DomainException>("the event id is the only thing stopping a redelivery from crediting twice");
    }

    [Fact]
    public void A_closed_period_charges_only_what_went_past_the_allowance()
    {
        var debit = AssistantBoostDebit.Close(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-30), Now, includedLimit: 3_000_000, usedInPeriod: 4_500_000, now: Now);

        debit.Tokens.Should().Be(1_500_000);
        debit.UsedInPeriod.Should().Be(4_500_000, "what the period spent in full is kept, not just the excess");
        debit.IncludedLimit.Should().Be(3_000_000, "the allowance is frozen — the setting can change later");
    }

    [Fact]
    public void A_period_inside_its_allowance_is_still_closed_with_zero()
    {
        var debit = AssistantBoostDebit.Close(
            Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-30), Now, 3_000_000, 1_200_000, Now);

        debit.Tokens.Should().Be(0, "zero says 'this period cost no boost' — absence would say 'never closed'");
    }

    [Fact]
    public void A_period_must_end_after_it_starts()
    {
        var backwards = () => AssistantBoostDebit.Close(
            Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddDays(-30), 3_000_000, 0, Now);

        backwards.Should().Throw<DomainException>();
    }
}
