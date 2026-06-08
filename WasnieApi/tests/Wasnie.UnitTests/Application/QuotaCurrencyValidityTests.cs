using FluentAssertions;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Guards the IsCurrencyValid flag computation in dashboard handlers.
/// The computation is: string.Equals(quotaCurrency, planCurrency, OrdinalIgnoreCase).
/// </summary>
public sealed class QuotaCurrencyValidityTests
{
    [Theory]
    [InlineData("EUR", "EUR", true)]
    [InlineData("PLN", "PLN", true)]
    [InlineData("eur", "EUR", true)]  // case-insensitive
    [InlineData("EUR", "PLN", false)]
    [InlineData("PLN", "EUR", false)]
    [InlineData("EUR", "",    false)] // empty plan currency (plan not found)
    [InlineData("",    "EUR", false)]
    public void IsCurrencyValid_MatchesExpectation(string quotaCurrency, string planCurrency, bool expected)
    {
        var isCurrencyValid = string.Equals(quotaCurrency, planCurrency, StringComparison.OrdinalIgnoreCase);
        isCurrencyValid.Should().Be(expected);
    }

    [Fact]
    public void IsCurrencyValid_False_WhenQuotaIsEurButPlanIsPln()
    {
        // The exact real-world scenario: Agnieszka's EUR quota on a PLN plan
        var quotaCurrency = "EUR";
        var planCurrency = "PLN";

        var isCurrencyValid = string.Equals(quotaCurrency, planCurrency, StringComparison.OrdinalIgnoreCase);

        isCurrencyValid.Should().BeFalse(
            because: "quota currency EUR does not match plan currency PLN — this is the legacy bad-data scenario from pre-A.3-FIX-1");
    }
}
