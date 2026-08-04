using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Dashboard;
using Wasnie.Application.Compensation.Queries.Dashboard;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// MONEY RULE — a payout is attributed to the period in which its cycle CLOSES (Period.End),
/// and to exactly one such period.
///
/// The dashboard used period INTERSECTION to sum payout totals, so a payout spanning several
/// months was counted IN FULL in every month it touched. A 2026-01-01→2026-12-31 payout landed
/// in all twelve months of 2026, which made the Banda 3 trend report identical current and prior
/// totals and a permanent 0% change. These tests lock the fix in.
/// </summary>
public sealed class DashboardPayoutPeriodAttributionTests : IDisposable
{
    // Fixed "today": 2026-08-04 → current period = Aug 1-4, prior period = Jul 1-31.
    private static readonly DateTime Today = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(Today, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly GetDashboardSummaryHandler _handler;

    public DashboardPayoutPeriodAttributionTests()
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
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Today);
        clock.UtcNowOffset.Returns(Now);

        _handler = new GetDashboardSummaryHandler(_db, auth, clock);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private void SeedPayout(DateOnly start, DateOnly end, decimal commission)
    {
        var spec = new PayoutLineSpec(
            CreditId: Guid.NewGuid(),
            RuleId: Guid.NewGuid(),
            RuleName: "Base",
            BaseAmount: Money.Of(commission * 20m, "EUR"),
            CommissionAmount: Money.Of(commission, "EUR"),
            AppliedModifiers: []);

        var payout = CompensationPayout.Calculate(
            TenantId, PayeeId, Guid.NewGuid(),
            PayeeReference.Snapshot(PayeeId, "Test Payee", "EMP-001"),
            DateRange.Of(start, end),
            [spec], "EUR", "test", Guid.NewGuid(), Now, Guid.NewGuid(),
            () => Guid.NewGuid());

        _db.CompensationPayouts.Add(payout);
        _db.SaveChanges();
    }

    private async Task<decimal> PayoutTotalAsync(string period)
    {
        var result = await _handler.Handle(new GetDashboardSummaryQuery(period), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var eur = result.Value!.PeriodBand.PayoutsTotalByCurrency
            .SingleOrDefault(c => c.Currency == "EUR");
        return eur?.Amount ?? 0m;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiMonthPayout_IsNotCountedInEveryMonthItSpans()
    {
        // The exact shape that produced the bug: one payout covering the whole of 2026.
        SeedPayout(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2_000m);

        var thisMonth = await PayoutTotalAsync("this-month");
        var lastMonth = await PayoutTotalAsync("last-month");

        thisMonth.Should().Be(0m,
            "the payout closes on 2026-12-31, so it belongs to December — not to August");
        lastMonth.Should().Be(0m,
            "it must not be double-counted into July either");
    }

    [Fact]
    public async Task PayoutIsAttributedToThePeriodWhereItCloses()
    {
        // Closes 2026-07-15 → belongs to July (the prior period), not to August.
        SeedPayout(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 15), 609.53m);

        (await PayoutTotalAsync("last-month")).Should().Be(609.53m,
            "the cycle closes inside July");
        (await PayoutTotalAsync("this-month")).Should().Be(0m,
            "July's payout must not leak into August");
    }

    [Fact]
    public async Task TrendBand_DoesNotReportEqualCurrentAndPriorForAMultiMonthPayout()
    {
        // One payout closing in July, one spanning July→September (closes in September).
        SeedPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 1_000m);
        SeedPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), 737.75m);

        var result = await _handler.Handle(
            new GetDashboardSummaryQuery("this-month"), CancellationToken.None);

        var trend = result.Value!.TrendBand!.CommissionTrend.Single(p => p.Currency == "EUR");

        trend.PriorAmount.Should().Be(1_000m,
            "only the payout closing in July belongs to the prior period");
        trend.CurrentAmount.Should().Be(0m,
            "nothing closes between Aug 1 and Aug 4; the Jul→Sep payout belongs to September");
        trend.CurrentAmount.Should().NotBe(trend.PriorAmount,
            "identical current/prior totals were the symptom of the double-counting bug");
    }

    [Fact]
    public async Task EachPayoutIsCountedExactlyOnceAcrossTheYear()
    {
        // Three payouts closing in three different months — the YTD total must be their plain sum.
        SeedPayout(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 100m);
        SeedPayout(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), 200m);
        SeedPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 300m);

        (await PayoutTotalAsync("ytd")).Should().Be(600m,
            "no payout may contribute more than its own amount to a period total");
    }
}
