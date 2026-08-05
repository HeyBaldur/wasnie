using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Dashboard;
using Wasnie.Application.Compensation.Queries.Dashboard;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// MONEY RULE — the dashboard "Payouts" widget is CASH FLOW: it reports money that actually left,
/// attributed to the day it left (PaidAt), and counts ONLY payouts in status Paid.
///
/// It previously summed every status and attributed by Period.End (the cycle close). Two real defects
/// came out of that, both reproduced below as regression tests:
///   · July 2026 showed €0 although €2,000 had been paid, because the money sat in a payout whose
///     period ran 2026-01-01→2026-12-31 and was therefore reported in December.
///   · June 2026 was inflated by €166K because a quarterly Apr–Jun run put April and May money there.
///
/// Attribution deliberately does NOT use the underlying transaction dates: those would let a
/// back-dated transaction rewrite a month that has already been reported.
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

    private CompensationPayout BuildPayout(DateOnly start, DateOnly end, decimal commission)
    {
        var spec = new PayoutLineSpec(
            CreditId: Guid.NewGuid(),
            RuleId: Guid.NewGuid(),
            RuleName: "Base",
            BaseAmount: Money.Of(commission * 20m, "EUR"),
            CommissionAmount: Money.Of(commission, "EUR"),
            AppliedModifiers: []);

        return CompensationPayout.Calculate(
            TenantId, PayeeId, Guid.NewGuid(),
            PayeeReference.Snapshot(PayeeId, "Test Payee", "EMP-001"),
            DateRange.Of(start, end),
            [spec], "EUR", "test", Guid.NewGuid(), Now, Guid.NewGuid(),
            () => Guid.NewGuid());
    }

    /// <summary>Seeds a payout carried all the way to Paid, with the payment landing on <paramref name="paidOn"/>.</summary>
    private void SeedPaidPayout(DateOnly start, DateOnly end, decimal commission, DateTimeOffset paidOn)
    {
        var payout = BuildPayout(start, end, commission);
        payout.Approve("test", paidOn, Guid.NewGuid());
        payout.MarkPaid("test", paidOn);

        _db.CompensationPayouts.Add(payout);
        _db.SaveChanges();
    }

    /// <summary>Seeds a payout left in Calculated — commission owed, no cash movement.</summary>
    private void SeedCalculatedPayout(DateOnly start, DateOnly end, decimal commission)
    {
        _db.CompensationPayouts.Add(BuildPayout(start, end, commission));
        _db.SaveChanges();
    }

    /// <summary>Seeds a payout left in Approved — authorised, but the money has not moved.</summary>
    private void SeedApprovedPayout(DateOnly start, DateOnly end, decimal commission, DateTimeOffset approvedOn)
    {
        var payout = BuildPayout(start, end, commission);
        payout.Approve("test", approvedOn, Guid.NewGuid());
        _db.CompensationPayouts.Add(payout);
        _db.SaveChanges();
    }

    private static DateTimeOffset On(int year, int month, int day, int hour = 12) =>
        new(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);

    private async Task<decimal> PayoutTotalAsync(string period)
    {
        var result = await _handler.Handle(new GetDashboardSummaryQuery(period), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var eur = result.Value!.PeriodBand.PayoutsTotalByCurrency
            .SingleOrDefault(c => c.Currency == "EUR");
        return eur?.Amount ?? 0m;
    }

    // ── attribution is by payment date, not by the payout's period ────────────

    [Fact]
    public async Task PayoutIsAttributedToTheMonthItWasPaid_NotTheMonthItsCycleCloses()
    {
        // THE PRODUCTION BUG, exactly: a full-year payout whose money actually moved on 2026-07-29.
        // Attributing by Period.End reported it in December and left July showing €0.
        SeedPaidPayout(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2_000m, On(2026, 7, 29));

        (await PayoutTotalAsync("last-month")).Should().Be(2_000m,
            "the money left the account on 2026-07-29, so it is July's cash flow");
        (await PayoutTotalAsync("this-month")).Should().Be(0m,
            "nothing was paid in August");
    }

    [Fact]
    public async Task QuarterlyRunPaidInsideTheQuarter_DoesNotInflateItsClosingMonth()
    {
        // The June inflation: an Apr–Jun quarterly run paid on 2026-06-18. Attribution by Period.End
        // also lands it in June here — but only because June is genuinely when the money moved. The
        // point of the test is that the SAME run paid in July belongs to July (next test).
        SeedPaidPayout(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), 166_679.48m, On(2026, 6, 18));

        (await PayoutTotalAsync("last-month")).Should().Be(0m, "it was paid in June, not July");
        (await PayoutTotalAsync("ytd")).Should().Be(166_679.48m, "it is counted once, in the year to date");
    }

    [Fact]
    public async Task PayoutWhoseCycleClosedEarlier_CountsInTheMonthTheMoneyMoved()
    {
        // Cycle closed 2026-06-26; payment was executed 2026-07-20. Cash flow says July.
        SeedPaidPayout(new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 26), 939.41m, On(2026, 7, 20));

        (await PayoutTotalAsync("last-month")).Should().Be(939.41m,
            "the payment happened in July even though the compensation period ended in June");
    }

    [Fact]
    public async Task MultiMonthPayout_IsCountedOnceNotInEveryMonthItSpans()
    {
        SeedPaidPayout(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2_000m, On(2026, 7, 29));

        var july = await PayoutTotalAsync("last-month");
        var august = await PayoutTotalAsync("this-month");
        var ytd = await PayoutTotalAsync("ytd");

        (july + august).Should().Be(2_000m, "the amount appears in exactly one month");
        ytd.Should().Be(2_000m, "and contributes its own amount once to the year, not twelve times");
    }

    // ── only Paid counts ──────────────────────────────────────────────────────

    [Fact]
    public async Task CalculatedPayout_IsExcluded()
    {
        SeedCalculatedPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 50_000m);

        (await PayoutTotalAsync("last-month")).Should().Be(0m,
            "commission that is merely calculated is money still owed, not cash that left");
        (await PayoutTotalAsync("all-time")).Should().Be(0m);
    }

    [Fact]
    public async Task ApprovedButUnpaidPayout_IsExcluded()
    {
        SeedApprovedPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 609.53m, On(2026, 7, 15));

        (await PayoutTotalAsync("last-month")).Should().Be(0m,
            "approval authorises the payment; it does not move the money");
        (await PayoutTotalAsync("all-time")).Should().Be(0m);
    }

    [Fact]
    public async Task OnlyThePaidPortionOfAMixedSetIsCounted()
    {
        SeedPaidPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 1_000m, On(2026, 7, 10));
        SeedApprovedPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 609.53m, On(2026, 7, 15));
        SeedCalculatedPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 50_000m);

        (await PayoutTotalAsync("last-month")).Should().Be(1_000m,
            "only the one payout that was actually paid contributes");
    }

    [Fact]
    public async Task RevertingAPaymentRemovesItFromCashFlow()
    {
        var payout = BuildPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 1_000m);
        payout.Approve("test", On(2026, 7, 10), Guid.NewGuid());
        payout.MarkPaid("test", On(2026, 7, 10));
        _db.CompensationPayouts.Add(payout);
        _db.SaveChanges();

        (await PayoutTotalAsync("last-month")).Should().Be(1_000m, "precondition: it counts while Paid");

        payout.RevertPaidToApproved("test", On(2026, 7, 20));
        _db.SaveChanges();

        (await PayoutTotalAsync("last-month")).Should().Be(0m,
            "a reverted payment did not happen, so it must leave the cash-flow total");
    }

    [Fact]
    public async Task RowViolatingTheInvariant_IsStillExcludedUnlessItIsPaid()
    {
        // The domain cannot produce this state (PaidAt is stamped only on the way into Paid and cleared
        // on the way out), so it can only arrive through data: a bad backfill, a manual UPDATE, a future
        // migration. The status filter in the query is what makes those rows harmless, and this test is
        // what makes the status filter non-redundant — without it, dropping `Status == Paid` from the
        // query changes nothing and the safeguard could be deleted by accident.
        var payout = BuildPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 99_999m);
        _db.CompensationPayouts.Add(payout);
        _db.SaveChanges();

        _db.Entry(payout).Property(nameof(CompensationPayout.PaidAt)).CurrentValue = On(2026, 7, 15);
        _db.SaveChanges();

        payout.Status.Should().Be(CompensationPayoutStatus.Calculated, "precondition: it is not Paid");

        (await PayoutTotalAsync("last-month")).Should().Be(0m,
            "cash flow counts payments, and an unpaid payout is not one no matter what date it carries");
    }

    // ── boundaries ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaymentLateOnTheFinalDayOfThePeriod_IsIncluded()
    {
        // 23:59 on 31 July. Comparing PaidAt against midnight would silently drop this payment —
        // an entire day of cash flow disappearing from the last day of every month.
        SeedPaidPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 500m,
            new DateTimeOffset(new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc), TimeSpan.Zero));

        (await PayoutTotalAsync("last-month")).Should().Be(500m,
            "the period is inclusive of the whole of its final day");
    }

    [Fact]
    public async Task PaymentAtTheFirstInstantOfThePeriod_IsIncluded()
    {
        SeedPaidPayout(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 300m,
            new DateTimeOffset(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.Zero));

        (await PayoutTotalAsync("last-month")).Should().Be(300m,
            "midnight on the first day belongs to the period");
    }

    [Fact]
    public async Task PaymentJustOutsideThePeriod_IsExcluded()
    {
        SeedPaidPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 400m,
            new DateTimeOffset(new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc), TimeSpan.Zero));

        (await PayoutTotalAsync("last-month")).Should().Be(0m,
            "one second before July is June's cash flow, not July's");
    }

    // ── trend band uses the same rule ─────────────────────────────────────────

    [Fact]
    public async Task TrendBand_ComparesCashPaidInEachPeriod()
    {
        // "today" is 2026-08-04, so this-month covers 1–4 August and — per the trend invariant — is
        // compared against the EQUIVALENT SLICE of July, 1–4 July, not against the whole month.
        // Two July payments, one inside that slice and one outside it, prove the window is honoured.
        SeedPaidPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 1_000m, On(2026, 7, 2));
        SeedPaidPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 9_999m, On(2026, 7, 15));
        SeedPaidPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), 737.75m, On(2026, 8, 3));

        var result = await _handler.Handle(
            new GetDashboardSummaryQuery("this-month"), CancellationToken.None);

        var trend = result.Value!.TrendBand!.CommissionTrend.Single(p => p.Currency == "EUR");

        trend.PriorAmount.Should().Be(1_000m,
            "only the payment inside 1–4 July belongs to the comparison slice; the 15 July payment is " +
            "outside the elapsed window and counting it would compare four days against thirty-one");
        trend.CurrentAmount.Should().Be(737.75m,
            "the Jul→Sep payout was paid on 3 August, so it is August cash regardless of its period");
    }

    [Fact]
    public async Task EachPaymentIsCountedExactlyOnceAcrossTheYear()
    {
        SeedPaidPayout(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 100m, On(2026, 4, 2));
        SeedPaidPayout(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), 200m, On(2026, 7, 1));
        SeedPaidPayout(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 300m, On(2026, 8, 1));

        (await PayoutTotalAsync("ytd")).Should().Be(600m,
            "no payment may contribute more than its own amount to a period total");
    }
}
