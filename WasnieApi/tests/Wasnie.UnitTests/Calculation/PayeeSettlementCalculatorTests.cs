using FluentAssertions;
using Wasnie.Domain.Compensation.Calculation;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// The cap and the carryover: how much of a debt a period's commissions actually absorb, and what
/// is left owing. This is what stops a clawback from zeroing someone's pay.
/// </summary>
public sealed class PayeeSettlementCalculatorTests
{
    private const string Eur = "EUR";

    private static readonly Guid PlanA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlanB = new("22222222-2222-2222-2222-222222222222");

    private static PayoutForSettlement Payout(Guid planId, decimal gross, decimal? cap) =>
        new(Guid.NewGuid(), planId, Money.Of(gross, Eur), cap);

    [Fact]
    public void No_debt_means_nothing_is_withheld()
    {
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 1000m, 50m)], Money.Zero(Eur));

        result.TotalWithheld.Amount.Should().Be(0m);
        result.Carryover.Amount.Should().Be(0m);
        result.Payouts.Single().Net.Amount.Should().Be(1000m);
    }

    [Fact]
    public void The_cap_limits_the_deduction_and_the_rest_carries_over()
    {
        // Debt 800, commissions 1000, cap 50% → 500 withheld, payee still takes home 500, 300 carries.
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 1000m, 50m)], Money.Of(800m, Eur));

        result.TotalWithheld.Amount.Should().Be(500m);
        result.Carryover.Amount.Should().Be(300m);
        var line = result.Payouts.Single();
        line.Withheld.Amount.Should().Be(500m);
        line.Net.Amount.Should().Be(500m);
    }

    [Fact]
    public void A_debt_smaller_than_the_cap_is_fully_collected_and_nothing_carries()
    {
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 1000m, 50m)], Money.Of(200m, Eur));

        result.TotalWithheld.Amount.Should().Be(200m);
        result.Carryover.Amount.Should().Be(0m);
        result.Payouts.Single().Net.Amount.Should().Be(800m);
    }

    [Fact]
    public void A_null_cap_allows_the_whole_payout_to_be_withheld()
    {
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 1000m, null)], Money.Of(1500m, Eur));

        result.TotalWithheld.Amount.Should().Be(1000m);
        result.Carryover.Amount.Should().Be(500m);
        result.Payouts.Single().Net.Amount.Should().Be(0m);
    }

    [Fact]
    public void A_zero_cap_protects_the_payout_entirely_and_the_debt_survives()
    {
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 1000m, 0m)], Money.Of(800m, Eur));

        result.TotalWithheld.Amount.Should().Be(0m);
        result.Carryover.Amount.Should().Be(800m);
        result.Payouts.Single().Net.Amount.Should().Be(1000m);
    }

    [Fact]
    public void The_debt_is_global_it_is_collected_across_two_plans()
    {
        // THE point of a per-payee ledger: a debt born under plan A is also collected from plan B.
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 400m, 50m), Payout(PlanB, 1000m, 50m)],
            Money.Of(600m, Eur));

        // Plan A ceiling 200 → 200 withheld; plan B ceiling 500 → the remaining 400 withheld.
        result.TotalWithheld.Amount.Should().Be(600m);
        result.Carryover.Amount.Should().Be(0m);
        result.Payouts.Single(p => p.PlanId == PlanA).Withheld.Amount.Should().Be(200m);
        result.Payouts.Single(p => p.PlanId == PlanB).Withheld.Amount.Should().Be(400m);
    }

    [Fact]
    public void Each_plan_applies_its_own_cap()
    {
        // Plan A protects its commissions fully (cap 0), plan B allows everything (cap 100).
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 500m, 0m), Payout(PlanB, 500m, 100m)],
            Money.Of(900m, Eur));

        result.Payouts.Single(p => p.PlanId == PlanA).Withheld.Amount.Should().Be(0m);
        result.Payouts.Single(p => p.PlanId == PlanB).Withheld.Amount.Should().Be(500m);
        result.Carryover.Amount.Should().Be(400m);
    }

    [Fact]
    public void A_debt_larger_than_every_cap_leaves_the_remainder_as_carryover()
    {
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 1000m, 50m), Payout(PlanB, 1000m, 50m)],
            Money.Of(5000m, Eur));

        result.TotalWithheld.Amount.Should().Be(1000m);
        result.Carryover.Amount.Should().Be(4000m);
        result.Payouts.Should().OnlyContain(p => p.Net.Amount == 500m);
    }

    [Fact]
    public void A_zero_payout_absorbs_nothing_and_the_debt_survives_intact()
    {
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 0m, 50m)], Money.Of(800m, Eur));

        result.TotalWithheld.Amount.Should().Be(0m);
        result.Carryover.Amount.Should().Be(800m);
    }

    [Fact]
    public void Gross_minus_withheld_always_equals_net_to_the_cent()
    {
        var result = PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 333.33m, 33.33m), Payout(PlanB, 666.67m, 66.67m)],
            Money.Of(1000m, Eur));

        foreach (var line in result.Payouts)
            line.Net.Amount.Should().Be(line.Gross.Amount - line.Withheld.Amount);

        result.TotalWithheld.Add(result.Carryover).Amount.Should().Be(1000m);
    }

    [Fact]
    public void No_payouts_at_all_carries_the_whole_debt()
    {
        var result = PayeeSettlementCalculator.Settle([], Money.Of(800m, Eur));

        result.Payouts.Should().BeEmpty();
        result.TotalWithheld.Amount.Should().Be(0m);
        result.Carryover.Amount.Should().Be(800m);
    }

    [Fact]
    public void The_order_of_collection_is_deterministic_regardless_of_input_order()
    {
        var a = Payout(PlanA, 400m, 50m);
        var b = Payout(PlanB, 1000m, 50m);

        var forward = PayeeSettlementCalculator.Settle([a, b], Money.Of(600m, Eur));
        var reversed = PayeeSettlementCalculator.Settle([b, a], Money.Of(600m, Eur));

        forward.Payouts.Select(p => p.PlanId).Should().Equal(reversed.Payouts.Select(p => p.PlanId));
        forward.Payouts.Single(p => p.PlanId == PlanA).Withheld.Amount
            .Should().Be(reversed.Payouts.Single(p => p.PlanId == PlanA).Withheld.Amount);
    }

    [Fact]
    public void A_payout_in_another_currency_is_refused_rather_than_converted()
    {
        var usd = new PayoutForSettlement(Guid.NewGuid(), PlanA, Money.Of(1000m, "USD"), 50m);

        var act = () => PayeeSettlementCalculator.Settle([usd], Money.Of(800m, Eur));

        act.Should().Throw<DomainException>().WithMessage("*no exchange rates*");
    }

    [Fact]
    public void A_negative_debt_is_refused_the_caller_must_pass_OutstandingDebt()
    {
        var act = () => PayeeSettlementCalculator.Settle(
            [Payout(PlanA, 1000m, 50m)], Money.Of(-800m, Eur));

        act.Should().Throw<DomainException>().WithMessage("*positive figure*");
    }
}
