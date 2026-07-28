using FluentAssertions;
using Wasnie.Domain.Compensation.Calculation;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// The formula that decides how much money is taken back from a person. Every figure here is
/// asserted to the cent — this is the arithmetic a rep will dispute, so it has to be reproducible.
/// </summary>
public sealed class ClawbackCalculatorTests
{
    private const string Eur = "EUR";

    // 900 EUR paid, 90-day maturation — the canonical example.
    [Theory]
    [InlineData(0, 900.00)]     // churned immediately → everything comes back
    [InlineData(30, 600.00)]    // a third of the window survived → two thirds back
    [InlineData(45, 450.00)]    // half
    [InlineData(60, 300.00)]
    [InlineData(89, 10.00)]     // one day short of fully earned
    [InlineData(90, 0.00)]      // matured exactly → nothing comes back
    [InlineData(120, 0.00)]     // outlived the window → floored at zero, never a bonus
    public void Proportional_clawback_to_the_cent(int daysActive, decimal expected)
    {
        var result = ClawbackCalculator.Proportional(Money.Of(900m, Eur), daysActive, 90);

        result.Amount.Should().Be(expected);
        result.Currency.Should().Be(Eur);
    }

    [Fact]
    public void The_floor_at_zero_holds_far_past_maturation()
    {
        ClawbackCalculator.Proportional(Money.Of(900m, Eur), 10_000, 90).Amount.Should().Be(0m);
    }

    [Fact]
    public void A_repeating_fraction_is_kept_at_system_precision_not_truncated()
    {
        // 100 × 1/3: no exact decimal exists. Money's 4-decimal banker's rounding (§5b.5) is the
        // system-wide rule; asserting it here so a change to Money surfaces as a clawback change.
        var result = ClawbackCalculator.Proportional(Money.Of(100m, Eur), 60, 90);

        result.Amount.Should().Be(33.3333m);
    }

    [Fact]
    public void Zero_commission_claws_back_zero()
    {
        ClawbackCalculator.Proportional(Money.Zero(Eur), 10, 90).Amount.Should().Be(0m);
    }

    [Fact]
    public void An_origin_error_claws_back_everything_regardless_of_time()
    {
        // No proportionality: a contract that was never real earns nothing by ageing.
        ClawbackCalculator.Full(Money.Of(900m, Eur)).Amount.Should().Be(900m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Maturation_days_must_be_positive(int maturationDays)
    {
        var act = () => ClawbackCalculator.Proportional(Money.Of(900m, Eur), 10, maturationDays);

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void Negative_days_active_is_refused_rather_than_guessed()
    {
        var act = () => ClawbackCalculator.Proportional(Money.Of(900m, Eur), -1, 90);

        act.Should().Throw<DomainException>().WithMessage("*cannot churn before it was won*");
    }

    [Fact]
    public void Negative_commission_is_refused()
    {
        var act = () => ClawbackCalculator.Proportional(Money.Of(-1m, Eur), 10, 90);

        act.Should().Throw<DomainException>().WithMessage("*must not be negative*");
    }

    // ── Days active from dates ────────────────────────────────────────────────

    [Fact]
    public void Days_active_counts_calendar_days_between_close_and_churn()
    {
        ClawbackCalculator.DaysActiveBetween(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1))
            .Should().Be(30);
    }

    [Fact]
    public void Churn_on_the_close_date_is_zero_days()
    {
        ClawbackCalculator.DaysActiveBetween(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1))
            .Should().Be(0);
    }

    [Fact]
    public void A_churn_date_before_the_close_date_is_zero_days_not_negative()
    {
        // Bad CRM data must not produce a clawback LARGER than the commission via a negative lifetime.
        ClawbackCalculator.DaysActiveBetween(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1))
            .Should().Be(0);
    }
}
