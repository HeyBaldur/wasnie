using FluentAssertions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class MoneyTests
{
    // ── Money.Of (allows any amount) ─────────────────────────────────────────

    [Fact]
    public void Of_ValidAmountAndCurrency_CreatesInstance()
    {
        var money = Money.Of(100m, "EUR");
        money.Amount.Should().Be(100m);
        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Of_LowercaseCurrency_NormalizesToUppercase()
    {
        var money = Money.Of(50m, "eur");
        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Of_EmptyCurrency_ThrowsDomainException()
    {
        var act = () => Money.Of(100m, "");
        act.Should().Throw<DomainException>().WithMessage("*3-letter ISO code*");
    }

    [Fact]
    public void Of_TwoCharCurrency_ThrowsDomainException()
    {
        var act = () => Money.Of(100m, "EU");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Of_FourCharCurrency_ThrowsDomainException()
    {
        var act = () => Money.Of(100m, "EURO");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Of_NegativeAmount_Succeeds_ForAdjustmentsAndClawbacks()
    {
        var money = Money.Of(-50m, "EUR");
        money.Amount.Should().Be(-50m);
    }

    [Fact]
    public void Of_LargeNegativeAmount_Succeeds()
    {
        var money = Money.Of(-999999.9999m, "EUR");
        money.Amount.Should().Be(-999999.9999m);
    }

    // ── Money.OfNonNegative (rejects negatives) ───────────────────────────────

    [Fact]
    public void OfNonNegative_WithPositiveAmount_Succeeds()
    {
        var money = Money.OfNonNegative(100m, "EUR");
        money.Amount.Should().Be(100m);
    }

    [Fact]
    public void OfNonNegative_WithZero_Succeeds()
    {
        var money = Money.OfNonNegative(0m, "EUR");
        money.Amount.Should().Be(0m);
    }

    [Fact]
    public void OfNonNegative_WithNegativeAmount_ThrowsDomainException()
    {
        var act = () => Money.OfNonNegative(-0.01m, "EUR");
        act.Should().Throw<DomainException>().WithMessage("*non-negative*");
    }

    [Fact]
    public void OfNonNegative_WithLargeNegative_ThrowsDomainException()
    {
        var act = () => Money.OfNonNegative(-1000m, "USD");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void OfNonNegative_InvalidCurrency_ThrowsDomainException()
    {
        var act = () => Money.OfNonNegative(100m, "EU");
        act.Should().Throw<DomainException>().WithMessage("*3-letter ISO code*");
    }

    // ── Money.Zero ───────────────────────────────────────────────────────────

    [Fact]
    public void Zero_ReturnsZeroAmount()
    {
        var zero = Money.Zero("USD");
        zero.Amount.Should().Be(0m);
        zero.Currency.Should().Be("USD");
    }

    // ── Arithmetic ────────────────────────────────────────────────────────────

    [Fact]
    public void Add_SameCurrency_ReturnsSummedAmount()
    {
        var a = Money.Of(100m, "EUR");
        var b = Money.Of(50m, "EUR");
        var result = a.Add(b);
        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Add_DifferentCurrencies_ThrowsDomainException()
    {
        var eur = Money.Of(100m, "EUR");
        var usd = Money.Of(100m, "USD");
        var act = () => eur.Add(usd);
        act.Should().Throw<DomainException>().WithMessage("*different currencies*");
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var a = Money.Of(100m, "EUR");
        var b = Money.Of(30m, "EUR");
        var result = a.Subtract(b);
        result.Amount.Should().Be(70m);
    }

    [Fact]
    public void Subtract_DifferentCurrencies_ThrowsDomainException()
    {
        var eur = Money.Of(100m, "EUR");
        var usd = Money.Of(30m, "USD");
        var act = () => eur.Subtract(usd);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Multiply_ByFactor_ReturnsScaledAmount()
    {
        var money = Money.Of(200m, "EUR");
        var result = money.Multiply(0.05m);
        result.Amount.Should().Be(10m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Divide_ByNonZero_ReturnsQuotient()
    {
        var money = Money.Of(100m, "EUR");
        var result = money.Divide(4m);
        result.Amount.Should().Be(25m);
    }

    [Fact]
    public void Divide_ByZero_ThrowsDomainException()
    {
        var money = Money.Of(100m, "EUR");
        var act = () => money.Divide(0m);
        act.Should().Throw<DomainException>().WithMessage("*divide*zero*");
    }

    [Fact]
    public void Decimal_Arithmetic_IsExact_NoFloatingPointError()
    {
        var a = Money.Of(0.1m + 0.2m, "EUR");
        a.Amount.Should().Be(0.3m);
    }

    // ── Equality ──────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        var a = Money.Of(100m, "EUR");
        var b = Money.Of(100m, "EUR");
        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentAmount_AreNotEqual()
    {
        var a = Money.Of(100m, "EUR");
        var b = Money.Of(200m, "EUR");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentCurrency_AreNotEqual()
    {
        var a = Money.Of(100m, "EUR");
        var b = Money.Of(100m, "USD");
        a.Should().NotBe(b);
    }

    // ── ToString — banker's rounding (MidpointRounding.ToEven) ───────────────

    [Fact]
    public void ToString_WholeNumber_FormatsCorrectly()
    {
        Money.Of(100m, "EUR").ToString().Should().Be("100.00 EUR");
    }

    [Fact]
    public void ToString_BankersRounding_HalfToEven_RoundsDown()
    {
        // 2.345 → digit at pos 2 is 4 (even), so 5 rounds down → 2.34
        Money.Of(2.345m, "EUR").ToString().Should().Be("2.34 EUR");
    }

    [Fact]
    public void ToString_BankersRounding_HalfToEven_RoundsUp()
    {
        // 2.355 → digit at pos 2 is 5 (odd), so 5 rounds up → 2.36
        Money.Of(2.355m, "EUR").ToString().Should().Be("2.36 EUR");
    }

    [Fact]
    public void ToString_1_005_RoundsToOneZeroZero()
    {
        // 1.005 → digit at pos 2 is 0 (even), rounds down → 1.00
        Money.Of(1.005m, "EUR").ToString().Should().Be("1.00 EUR");
    }

    [Fact]
    public void ToString_1_015_RoundsToOneZeroTwo()
    {
        // 1.015 → digit at pos 2 is 1 (odd), rounds up → 1.02
        Money.Of(1.015m, "EUR").ToString().Should().Be("1.02 EUR");
    }

    [Fact]
    public void ToString_1_125_RoundsToOneOneTwo()
    {
        // 1.125 → digit at pos 2 is 2 (even), rounds down → 1.12
        Money.Of(1.125m, "EUR").ToString().Should().Be("1.12 EUR");
    }

    [Fact]
    public void ToString_1_135_RoundsToOneOneFour()
    {
        // 1.135 → digit at pos 2 is 3 (odd), rounds up → 1.14
        Money.Of(1.135m, "EUR").ToString().Should().Be("1.14 EUR");
    }

    [Fact]
    public void ToString_NegativeValue_RoundsCorrectly()
    {
        // -1.005 → rounds to -1.00 (nearest even)
        Money.Of(-1.005m, "EUR").ToString().Should().Be("-1.00 EUR");
    }

    [Fact]
    public void ToString_LargeNumber_RoundsCorrectly()
    {
        Money.Of(123456789.005m, "EUR").ToString().Should().Be("123456789.00 EUR");
    }

    [Fact]
    public void ToString_InternalPrecision_NotAffectedByDisplay()
    {
        var money = Money.Of(1.2345m, "EUR");
        money.Amount.Should().Be(1.2345m);
        money.ToString().Should().Be("1.23 EUR");
        money.Amount.Should().Be(1.2345m);
    }
}
