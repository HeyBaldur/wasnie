using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

public sealed class QuotaTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateRange Period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

    [Fact]
    public void Create_MatchingCurrency_Succeeds()
    {
        var quota = Quota.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Money.Of(50_000m, "EUR"), Period,
            QuotaMeasurementType.Revenue,
            "user", Guid.NewGuid(), Now,
            planCurrency: "EUR");

        quota.Amount.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Create_MismatchedCurrency_ThrowsDomainException()
    {
        var act = () => Quota.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Money.Of(50_000m, "PLN"), Period,
            QuotaMeasurementType.Revenue,
            "user", Guid.NewGuid(), Now,
            planCurrency: "EUR");

        act.Should().Throw<DomainException>()
            .WithMessage("*PLN*EUR*");
    }

    [Fact]
    public void Create_NoPlanCurrencyPassed_DoesNotValidate()
    {
        // When planCurrency is null (legacy/migration path), no validation is applied.
        var quota = Quota.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Money.Of(50_000m, "USD"), Period,
            QuotaMeasurementType.Revenue,
            "user", Guid.NewGuid(), Now);

        quota.Amount.Currency.Should().Be("USD");
    }

    [Fact]
    public void UpdateDraft_MatchingCurrency_Succeeds()
    {
        var quota = Quota.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Money.Of(50_000m, "EUR"), Period,
            QuotaMeasurementType.Revenue,
            "user", Guid.NewGuid(), Now,
            planCurrency: "EUR");

        quota.UpdateDraft(
            Money.Of(75_000m, "EUR"), Period,
            QuotaMeasurementType.Revenue, null,
            "user", Now,
            planCurrency: "EUR");

        quota.Amount.Amount.Should().Be(75_000m);
    }

    [Fact]
    public void UpdateDraft_MismatchedCurrency_ThrowsDomainException()
    {
        var quota = Quota.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Money.Of(50_000m, "EUR"), Period,
            QuotaMeasurementType.Revenue,
            "user", Guid.NewGuid(), Now,
            planCurrency: "EUR");

        var act = () => quota.UpdateDraft(
            Money.Of(75_000m, "PLN"), Period,
            QuotaMeasurementType.Revenue, null,
            "user", Now,
            planCurrency: "EUR");

        act.Should().Throw<DomainException>()
            .WithMessage("*PLN*EUR*");
    }
}
