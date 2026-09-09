using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Dashboard;
using Wasnie.Application.Compensation.Queries.Dashboard;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-62 — the Total / Paid / Unpaid commission cards.
///
/// MONEY RULE: <c>Total = Paid + Unpaid</c>, to the cent, over the same range. The handler reads the
/// range's credits ONCE and partitions that single set; these tests exist to pin that the partition
/// really is a partition — every credit lands in exactly one bucket, and nothing is counted twice or
/// silently dropped.
/// </summary>
public sealed class DashboardCommissionsBandTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(Today, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();

    private static readonly DateOnly AugFrom = new(2026, 8, 1);
    private static readonly DateOnly AugTo = new(2026, 8, 31);

    private readonly ApplicationDbContext _db;
    private readonly GetDashboardSummaryHandler _handler;

    public DashboardCommissionsBandTests()
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

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Today);
        clock.UtcNowOffset.Returns(Now);

        _handler = new GetDashboardSummaryHandler(_db, auth, clock);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private Credit SeedCredit(
        decimal amount,
        DateTimeOffset allocatedAt,
        string currency = "EUR",
        bool paid = false,
        bool superseded = false,
        CreditClosureReason? closedAs = null)
    {
        var ruleId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        var credit = Credit.Allocate(
            TenantId, Guid.NewGuid(), PayeeId, planId, ruleId,
            RuleSnapshot.Freeze(ruleId, planId, 1, "Base", RateTable.Flat(0.05m), Trigger.Always(), allocatedAt),
            Money.Of(amount * 20m, currency), Money.Of(amount, currency),
            Percentage.FromPercent(5m), CreditRole.Primary,
            "seed", Guid.NewGuid(), allocatedAt, Guid.NewGuid());

        if (paid) credit.Consume(Guid.NewGuid(), allocatedAt.AddDays(1), Guid.NewGuid());
        if (superseded) credit.Supersede("reassigned", allocatedAt.AddDays(1), Guid.NewGuid());
        if (closedAs is { } reason) credit.Close(reason, "closed by test", "seed", allocatedAt.AddDays(1), Guid.NewGuid());

        _db.Credits.Add(credit);
        _db.SaveChanges();
        return credit;
    }

    private static DateTimeOffset On(int year, int month, int day, int hour = 12) =>
        new(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);

    private async Task<DashboardCommissionsBandDto> BandAsync(DateOnly? from = null, DateOnly? to = null)
    {
        var result = await _handler.Handle(
            new GetDashboardSummaryQuery(from ?? AugFrom, to ?? AugTo), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value!.CommissionsBand;
    }

    private static decimal Eur(IReadOnlyList<CurrencyTotalDto> totals) =>
        totals.SingleOrDefault(t => t.Currency == "EUR")?.Amount ?? 0m;

    // ── the invariant ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TotalEqualsPaidPlusUnpaid_ToTheCent()
    {
        SeedCredit(1_234.56m, On(2026, 8, 2), paid: true);
        SeedCredit(99.99m, On(2026, 8, 10));
        SeedCredit(0.01m, On(2026, 8, 20), paid: true);
        SeedCredit(4_000.44m, On(2026, 8, 28));

        var band = await BandAsync();

        Eur(band.PaidByCurrency).Should().Be(1_234.57m);
        Eur(band.UnpaidByCurrency).Should().Be(4_100.43m);
        Eur(band.TotalByCurrency).Should().Be(5_335.00m);
        Eur(band.TotalByCurrency).Should().Be(Eur(band.PaidByCurrency) + Eur(band.UnpaidByCurrency));
    }

    [Fact]
    public async Task TheInvariantHoldsInEveryCurrencyIndependently()
    {
        SeedCredit(100m, On(2026, 8, 2), "EUR", paid: true);
        SeedCredit(50m, On(2026, 8, 3), "EUR");
        SeedCredit(700m, On(2026, 8, 4), "GBP", paid: true);
        SeedCredit(9m, On(2026, 8, 5), "PLN");

        var band = await BandAsync();

        foreach (var total in band.TotalByCurrency)
        {
            var paid = band.PaidByCurrency.SingleOrDefault(t => t.Currency == total.Currency)?.Amount ?? 0m;
            var unpaid = band.UnpaidByCurrency.SingleOrDefault(t => t.Currency == total.Currency)?.Amount ?? 0m;
            total.Amount.Should().Be(paid + unpaid, $"the three cards must agree for {total.Currency}");
        }

        band.TotalByCurrency.Select(t => t.Currency).Should().BeEquivalentTo(["EUR", "GBP", "PLN"]);
    }

    // ── what belongs in the buckets ───────────────────────────────────────────

    [Fact]
    public async Task AConsumedCreditIsPaid_AndAnOutstandingOneIsNot()
    {
        SeedCredit(300m, On(2026, 8, 2), paid: true);
        SeedCredit(200m, On(2026, 8, 3));

        var band = await BandAsync();

        Eur(band.PaidByCurrency).Should().Be(300m);
        Eur(band.UnpaidByCurrency).Should().Be(200m);
    }

    [Fact]
    public async Task ASupersededCreditIsInNoBucketAtAll()
    {
        // It was replaced by a reallocation that is itself in the set. Counting both would double the
        // commission of every recalculated sale.
        SeedCredit(500m, On(2026, 8, 2), superseded: true);
        SeedCredit(500m, On(2026, 8, 2));

        var band = await BandAsync();

        Eur(band.TotalByCurrency).Should().Be(500m);
        Eur(band.ClosedTotalByCurrency).Should().Be(0m);
    }

    [Fact]
    public async Task AWrittenOffCreditIsNeitherPaidNorUnpaid_AndIsReportedSeparately()
    {
        // Calling it Unpaid would say the company still owes money it decided not to pay; calling it
        // Paid would claim a write-off as a payment. Both are lies about money.
        SeedCredit(1_000m, On(2026, 8, 2), paid: true);
        SeedCredit(3_869.34m, On(2026, 8, 3), closedAs: CreditClosureReason.WrittenOff);

        var band = await BandAsync();

        Eur(band.PaidByCurrency).Should().Be(1_000m);
        Eur(band.UnpaidByCurrency).Should().Be(0m);
        Eur(band.TotalByCurrency).Should().Be(1_000m);
        Eur(band.ClosedTotalByCurrency).Should().Be(3_869.34m,
            "the omission has to be visible somewhere, or the money simply disappears from the screen");
    }

    [Fact]
    public async Task ACreditSettledOutsideWasnieIsTreatedTheSameWay()
    {
        SeedCredit(800m, On(2026, 8, 2), closedAs: CreditClosureReason.ExternalSettlement);

        var band = await BandAsync();

        Eur(band.TotalByCurrency).Should().Be(0m);
        Eur(band.ClosedTotalByCurrency).Should().Be(800m);
    }

    // ── the range ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreditsOutsideTheRangeAreExcluded_OnBothSides()
    {
        SeedCredit(10m, On(2026, 7, 31, 23));  // the day before
        SeedCredit(20m, On(2026, 8, 1, 0));    // first instant of the range
        SeedCredit(40m, On(2026, 8, 31, 23));  // last instant of the range
        SeedCredit(80m, On(2026, 9, 1, 0));    // the day after

        var band = await BandAsync();

        Eur(band.TotalByCurrency).Should().Be(60m,
            "the range is inclusive of both whole end days and of nothing beyond them");
    }

    [Fact]
    public async Task ARangeWithNoCommissionsReportsZero_NotAnError()
    {
        SeedCredit(500m, On(2026, 8, 2));

        var band = await BandAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));

        band.TotalByCurrency.Should().BeEmpty();
        band.PaidByCurrency.Should().BeEmpty();
        band.UnpaidByCurrency.Should().BeEmpty();
        Eur(band.TotalByCurrency).Should().Be(0m);
    }

    [Fact]
    public async Task AnArbitraryRangeIsHonoured_NotSnappedToAMonth()
    {
        SeedCredit(100m, On(2026, 2, 15));
        SeedCredit(200m, On(2026, 3, 20));
        SeedCredit(400m, On(2026, 4, 14));
        SeedCredit(800m, On(2026, 4, 16));  // one day past the end

        var band = await BandAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 15));

        Eur(band.TotalByCurrency).Should().Be(700m);
    }

    // ── defaults and refusals ─────────────────────────────────────────────────

    [Fact]
    public async Task WithNoRangeGiven_TheWholeCurrentMonthIsUsed()
    {
        SeedCredit(10m, On(2026, 7, 31));   // previous month
        SeedCredit(20m, On(2026, 8, 1));    // first day of the current month
        SeedCredit(40m, On(2026, 8, 31));   // last day — in the FUTURE relative to "today" (Aug 4)
        SeedCredit(80m, On(2026, 9, 1));    // next month

        var result = await _handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.From.Should().Be(new DateOnly(2026, 8, 1));
        result.Value.To.Should().Be(new DateOnly(2026, 8, 31),
            "the default is the whole month, not the month so far");
        Eur(result.Value.CommissionsBand.TotalByCurrency).Should().Be(60m);
    }

    [Fact]
    public async Task ABackwardsRangeIsRefusedWithACode_NotSilentlySwapped()
    {
        var result = await _handler.Handle(
            new GetDashboardSummaryQuery(new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GetDashboardSummaryHandler.InvalidRangeCode);
    }

    [Fact]
    public async Task ASingleDayRangeIsValid()
    {
        SeedCredit(75m, On(2026, 8, 10));
        SeedCredit(25m, On(2026, 8, 11));

        var band = await BandAsync(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        Eur(band.TotalByCurrency).Should().Be(75m);
    }

    // ── the range governs the rest of the page, but not the action band ───────

    [Fact]
    public async Task TheActionBandIgnoresTheRange()
    {
        // Action items are work to be done, not a report. A draft pay run raised in September still has
        // to be approved while the reader is looking at February, so filtering it away would hide it.
        var february = await _handler.Handle(
            new GetDashboardSummaryQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            CancellationToken.None);
        var august = await _handler.Handle(
            new GetDashboardSummaryQuery(AugFrom, AugTo), CancellationToken.None);

        february.Value!.ActionBand.Should().BeEquivalentTo(august.Value!.ActionBand);
    }
}
