using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-63 — the payee page's Total / Paid / Unpaid cards.
///
/// They are the dashboard's three figures narrowed to one person, built by the SAME spec. These tests
/// pin that narrowing: the invariant still holds per payee, and one payee's money never leaks into
/// another's card.
/// </summary>
public sealed class PayeeCommissionsBandTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly Guid Grace = Guid.NewGuid();

    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    private readonly ApplicationDbContext _db;

    public PayeeCommissionsBandTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());
    }

    public void Dispose() => _db.Dispose();

    private void SeedCredit(
        Guid payeeId, decimal amount, DateTimeOffset allocatedAt,
        bool paid = false, CreditClosureReason? closedAs = null)
    {
        var ruleId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var credit = Credit.Allocate(
            TenantId, Guid.NewGuid(), payeeId, planId, ruleId,
            RuleSnapshot.Freeze(ruleId, planId, 1, "Base", RateTable.Flat(0.05m), Trigger.Always(), allocatedAt),
            Money.Of(amount * 20m, "EUR"), Money.Of(amount, "EUR"),
            Percentage.FromPercent(5m), CreditRole.Primary,
            "seed", Guid.NewGuid(), allocatedAt, Guid.NewGuid());

        if (paid) credit.Consume(Guid.NewGuid(), allocatedAt.AddDays(1), Guid.NewGuid());
        if (closedAs is { } r) credit.Close(r, "closed by test", "seed", allocatedAt.AddDays(1), Guid.NewGuid());

        _db.Credits.Add(credit);
        _db.SaveChanges();
    }

    private static DateTimeOffset On(int d, int hour = 12) =>
        new(new DateTime(2026, 8, d, hour, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);

    private Task<DashboardCommissionsBandDto> BandAsync(Guid? payeeId) =>
        CommissionsBandSpec.BuildAsync(_db, From, To, payeeId, CancellationToken.None);

    private static decimal Eur(IReadOnlyList<CurrencyTotalDto> t) =>
        t.SingleOrDefault(x => x.Currency == "EUR")?.Amount ?? 0m;

    [Fact]
    public async Task ThePayeeBandCountsOnlyThatPayee()
    {
        SeedCredit(Ada, 100m, On(2), paid: true);
        SeedCredit(Ada, 50m, On(3));
        SeedCredit(Grace, 900m, On(4), paid: true);

        var ada = await BandAsync(Ada);

        Eur(ada.PaidByCurrency).Should().Be(100m);
        Eur(ada.UnpaidByCurrency).Should().Be(50m);
        Eur(ada.TotalByCurrency).Should().Be(150m, "Grace's 900 belongs on Grace's page, not Ada's");
    }

    [Fact]
    public async Task TheInvariantHoldsForASinglePayee()
    {
        SeedCredit(Ada, 1_234.56m, On(2), paid: true);
        SeedCredit(Ada, 0.01m, On(3), paid: true);
        SeedCredit(Ada, 99.99m, On(4));

        var band = await BandAsync(Ada);

        Eur(band.TotalByCurrency)
            .Should().Be(Eur(band.PaidByCurrency) + Eur(band.UnpaidByCurrency));
        Eur(band.TotalByCurrency).Should().Be(1_334.56m);
    }

    [Fact]
    public async Task TheSumOfEveryPayeeEqualsTheTenantWideBand()
    {
        // The dashboard's figure and the payee pages are the same money seen at two scopes. If they
        // ever stop adding up, one of the two screens is lying.
        SeedCredit(Ada, 100m, On(2), paid: true);
        SeedCredit(Ada, 50m, On(3));
        SeedCredit(Grace, 900m, On(4), paid: true);
        SeedCredit(Grace, 7m, On(5));

        var tenant = await BandAsync(null);
        var ada = await BandAsync(Ada);
        var grace = await BandAsync(Grace);

        Eur(tenant.TotalByCurrency).Should().Be(Eur(ada.TotalByCurrency) + Eur(grace.TotalByCurrency));
        Eur(tenant.PaidByCurrency).Should().Be(Eur(ada.PaidByCurrency) + Eur(grace.PaidByCurrency));
        Eur(tenant.UnpaidByCurrency).Should().Be(Eur(ada.UnpaidByCurrency) + Eur(grace.UnpaidByCurrency));
    }

    [Fact]
    public async Task AClosedCreditIsOutOfThePayeeCardsToo()
    {
        SeedCredit(Ada, 100m, On(2), paid: true);
        SeedCredit(Ada, 3_869.34m, On(3), closedAs: CreditClosureReason.WrittenOff);

        var band = await BandAsync(Ada);

        Eur(band.TotalByCurrency).Should().Be(100m);
        Eur(band.UnpaidByCurrency).Should().Be(0m);
        Eur(band.ClosedTotalByCurrency).Should().Be(3_869.34m);
    }

    [Fact]
    public async Task CreditsOutsideTheRangeAreExcludedOnBothSides()
    {
        SeedCredit(Ada, 10m, new DateTimeOffset(new DateTime(2026, 7, 31, 23, 0, 0, DateTimeKind.Utc), TimeSpan.Zero));
        SeedCredit(Ada, 20m, On(1, 0));
        SeedCredit(Ada, 40m, On(31, 23));
        SeedCredit(Ada, 80m, new DateTimeOffset(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.Zero));

        Eur((await BandAsync(Ada)).TotalByCurrency).Should().Be(60m);
    }

    [Fact]
    public async Task APayeeWithNoCommissionsInTheRangeReportsNothing_NotAnError()
    {
        SeedCredit(Grace, 500m, On(2));

        var band = await BandAsync(Ada);

        band.TotalByCurrency.Should().BeEmpty();
        Eur(band.TotalByCurrency).Should().Be(0m);
    }
}
