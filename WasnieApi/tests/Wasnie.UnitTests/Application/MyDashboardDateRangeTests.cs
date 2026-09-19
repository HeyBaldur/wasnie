using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Application.Features.Users.Handlers;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Enums;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-98: the personal dashboard is driven by a date range.
///
/// ★★ THE DEFECT, IN ONE SENTENCE. A person could not look at their own pay for a month that had
/// closed. The money was all-time and the quotas were whatever happened to be running today, so "how
/// much was I paid two months ago" — the single most ordinary question anybody has about their own
/// commission — had nowhere on this screen to be asked. The administrator's dashboard has had a range
/// since KAN-62.
///
/// ★★ THE THREE THINGS THAT MUST NOT MOVE WITH THE WINDOW ARE TESTED AS HARD AS THE ONES THAT MUST.
/// Adding a control is the easy half; the half that goes wrong quietly is a figure that starts
/// answering a question it cannot answer. Debt and awaiting-payment have no period dimension at all,
/// and the stuck-sale notice is a standing warning rather than a measurement — scoping any of them to
/// the dates would make somebody's real problem vanish by moving a picker.
/// </summary>
public sealed class MyDashboardDateRangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 18);

    private const string MyUser = "user-mine";

    private sealed record Harness(
        ApplicationDbContext Db,
        GetMyDashboardHandler Handler,
        ISender Sender,
        IQuotaAttainmentService Attainment,
        Guid TenantId,
        Guid MyPayeeId);

    private static Harness Seed(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<IPublisher>());

        var mine = Payee.Create(tenantId, "Ana Garcia", "EMP-MINE", "ana@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        mine.LinkToUser(MyUser, "test", Now);
        db.Payees.Add(mine);
        db.SaveChanges();

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(MyUser);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.UtcDateTime);
        clock.UtcNowOffset.Returns(Now);

        // The money query is stubbed: what is under test here is WHICH WINDOW it is asked for, not what
        // it answers. An unconfigured substitute returns null and the handler NREs on `summary.IsSuccess`.
        var sender = Substitute.For<ISender>();
        sender
            .Send(Arg.Any<IRequest<Result<PayeeLedgerSummaryDto>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<PayeeLedgerSummaryDto>.Success(new PayeeLedgerSummaryDto(
                mine.Id, mine.FullName, "custom", null, null, [])));

        var attainment = Substitute.For<IQuotaAttainmentService>();
        attainment
            .ComputeAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new AttainmentReading(
                AttainmentPercentage.FromAchievedAndTarget(0m, 1000m), AttainmentSource.Measured));

        return new Harness(
            db,
            new GetMyDashboardHandler(db, auth, currentUser, attainment, sender, clock),
            sender,
            attainment,
            tenantId,
            mine.Id);
    }

    /// <summary>An Active quota over an explicit period, on its own plan.</summary>
    private static Guid AddQuota(Harness h, DateOnly start, DateOnly end, string planName)
    {
        var period = DateRange.Of(start, end);

        var plan = Plan.Create(
            h.TenantId, planName, "test plan", period, "EUR",
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        h.Db.CompensationPlans.Add(plan);

        var quota = Quota.Create(
            h.TenantId, h.MyPayeeId, plan.Id, Money.Of(1000m, "EUR"), period,
            QuotaMeasurementType.Revenue, "test", Guid.NewGuid(), Now);
        quota.Activate("test", Now, Guid.NewGuid());

        h.Db.Quotas.Add(quota);
        h.Db.SaveChanges();

        return quota.Id;
    }

    private static void AddStuckSale(Harness h, DateOnly saleDate)
    {
        var tx = CompensationTransaction.Ingest(
            h.TenantId,
            $"REF-{Guid.NewGuid():N}"[..12],
            h.MyPayeeId,
            Money.Of(5000m, "EUR"),
            saleDate,
            TransactionSource.Manual,
            "test",
            Guid.NewGuid(),
            Now,
            Guid.NewGuid());

        h.Db.CompensationTransactions.Add(tx);
        h.Db.SaveChanges();
    }

    // ── The window reaches the money ──────────────────────────────────────

    /// <summary>
    /// ★★ THE WINDOW IS FORWARDED VERBATIM TO THE QUERY THAT OWNS THE MONEY. The interesting way to get
    /// this wrong is to accept the parameters, echo them back in the answer, and never pass them on —
    /// which produces a screen whose heading says July and whose figures are all-time. Nothing visible
    /// distinguishes that from working.
    /// </summary>
    [Fact]
    public async Task The_window_is_passed_to_the_ledger_summary()
    {
        var h = Seed(nameof(The_window_is_passed_to_the_ledger_summary));
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 31);

        await h.Handler.Handle(new GetMyDashboardQuery(from, to), default);

        await h.Sender.Received(1).Send(
            Arg.Is<GetPayeeLedgerSummaryQuery>(q =>
                q.PayeeId == h.MyPayeeId && q.From == from && q.To == to),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ★ NO WINDOW MEANS NO WINDOW, not "the current month". The client chooses what it opens on; a
    /// server that quietly picked one would make an unparameterised call mean a different thing every
    /// month, and the assistant calls this handler too.
    /// </summary>
    [Fact]
    public async Task No_window_asks_the_ledger_summary_for_none()
    {
        var h = Seed(nameof(No_window_asks_the_ledger_summary_for_none));

        await h.Handler.Handle(new GetMyDashboardQuery(), default);

        await h.Sender.Received(1).Send(
            Arg.Is<GetPayeeLedgerSummaryQuery>(q => q.From == null && q.To == null),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ★ THE APPLIED WINDOW COMES BACK, so the screen can state it rather than trust its own control.
    /// The two differ for the whole of a request, which is exactly when somebody reads the heading.
    /// </summary>
    [Fact]
    public async Task The_applied_window_is_echoed_back()
    {
        var h = Seed(nameof(The_applied_window_is_echoed_back));
        var from = new DateOnly(2026, 2, 1);
        var to = new DateOnly(2026, 4, 15);

        var result = await h.Handler.Handle(new GetMyDashboardQuery(from, to), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.From.Should().Be(from);
        result.Value.To.Should().Be(to);
    }

    // ── The window reaches the quotas ─────────────────────────────────────

    /// <summary>
    /// ★★ THE POINT OF THE WHOLE TICKET. A quota that ended before today was unreachable: the screen
    /// could only ever show what was running right now, so the person asking how last quarter went had
    /// nowhere to look.
    /// </summary>
    [Fact]
    public async Task A_closed_quota_inside_the_window_is_returned()
    {
        var h = Seed(nameof(A_closed_quota_inside_the_window_is_returned));
        AddQuota(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "July plan");

        var result = await h.Handler.Handle(
            new GetMyDashboardQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), default);

        result.Value.Quotas.Should().ContainSingle()
            .Which.PlanName.Should().Be("July plan");
    }

    /// <summary>★ And a quota outside the window is not, or the range would be decoration.</summary>
    [Fact]
    public async Task A_quota_outside_the_window_is_not_returned()
    {
        var h = Seed(nameof(A_quota_outside_the_window_is_not_returned));
        AddQuota(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "July plan");

        var result = await h.Handler.Handle(
            new GetMyDashboardQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), default);

        result.Value.Quotas.Should().BeEmpty();
    }

    /// <summary>
    /// ★★ OVERLAPPING AT ALL IS ENOUGH. A quarterly target seen through a one-month window is still
    /// that person's target for the days they are looking at; requiring containment would hide every
    /// quota whose period is longer than the window, which is most of them.
    /// </summary>
    [Fact]
    public async Task A_quota_merely_overlapping_the_window_is_returned()
    {
        var h = Seed(nameof(A_quota_merely_overlapping_the_window_is_returned));
        AddQuota(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), "Q3 plan");

        var result = await h.Handler.Handle(
            new GetMyDashboardQuery(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), default);

        result.Value.Quotas.Should().ContainSingle();
    }

    /// <summary>
    /// ★★ NO WINDOW STILL MEANS "IN EFFECT TODAY", which is what this screen did before the range
    /// existed. It is not a second code path — both bounds default to today and the intersection with
    /// the single day [today, today] is the old rule — but the behaviour is what callers depend on, so
    /// it is pinned here rather than left to be inferred from the implementation.
    /// </summary>
    [Fact]
    public async Task Without_a_window_only_todays_quota_is_returned()
    {
        var h = Seed(nameof(Without_a_window_only_todays_quota_is_returned));
        AddQuota(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "July plan");
        AddQuota(h, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "September plan");

        var result = await h.Handler.Handle(new GetMyDashboardQuery(), default);

        result.Value.Quotas.Should().ContainSingle()
            .Which.PlanName.Should().Be("September plan");
    }

    // ── Where the ratio is measured ───────────────────────────────────────

    /// <summary>
    /// ★★ THE RATIO IS MEASURED INSIDE THE QUOTA, OR IT IS ABOUT A DIFFERENT QUOTA. The attainment
    /// service takes a date and resolves whichever quota is in effect on it. Asked about a day past the
    /// end of a closed one — which a window of a whole year is, for a July target — it answers about the
    /// NEXT period, and the screen prints that ratio under this target: two numbers that were never
    /// about each other. The clamp is the whole of the difference and nothing on screen would show it
    /// missing.
    /// </summary>
    [Fact]
    public async Task A_closed_quota_is_measured_at_its_own_end_not_at_the_window_end()
    {
        var h = Seed(nameof(A_closed_quota_is_measured_at_its_own_end_not_at_the_window_end));
        AddQuota(h, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "July plan");

        await h.Handler.Handle(
            new GetMyDashboardQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), default);

        await h.Attainment.Received(1).ComputeAsync(
            h.MyPayeeId, Arg.Any<Guid>(), new DateOnly(2026, 7, 31), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ★★ AND NEVER LATER THAN TODAY. A window running to the end of the month names days that have not
    /// happened; measuring a running quota there reports a partial month as though it were over, and the
    /// person reads a shortfall that is only the calendar.
    /// </summary>
    [Fact]
    public async Task A_running_quota_is_measured_today_not_at_a_future_window_end()
    {
        var h = Seed(nameof(A_running_quota_is_measured_today_not_at_a_future_window_end));
        AddQuota(h, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "September plan");

        await h.Handler.Handle(
            new GetMyDashboardQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), default);

        await h.Attainment.Received(1).ComputeAsync(
            h.MyPayeeId, Arg.Any<Guid>(), Today, Arg.Any<CancellationToken>());
    }

    // ── What the window must NOT touch ────────────────────────────────────

    /// <summary>
    /// ★★ THE STUCK-SALE NOTICE IS NOT A FIGURE ABOUT THE WINDOW. It is a standing warning that
    /// something of theirs cannot be paid. Scoping it to the dates would mean the sale disappears the
    /// moment the reader looks at a different month — the exact invisibility KAN-94 existed to end,
    /// reintroduced through the new control, and silently: the page would look perfectly healthy.
    /// </summary>
    [Fact]
    public async Task A_stuck_sale_outside_the_window_is_still_reported()
    {
        var h = Seed(nameof(A_stuck_sale_outside_the_window_is_still_reported));
        AddStuckSale(h, new DateOnly(2026, 9, 17));

        var result = await h.Handler.Handle(
            new GetMyDashboardQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), default);

        result.Value.SalesAwaitingSetup.Should().Be(1);
    }

    /// <summary>
    /// ★ THE PAYEE IS STILL RESOLVED FROM THE TOKEN. The two new values say WHICH DAYS, never WHOSE, and
    /// this is the assertion that keeps it that way if somebody later reaches for "just add a payeeId
    /// while we are in here".
    /// </summary>
    [Fact]
    public async Task An_unlinked_user_gets_no_figures_whatever_window_they_ask_for()
    {
        var h = Seed(nameof(An_unlinked_user_gets_no_figures_whatever_window_they_ask_for));

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("somebody-with-no-payee");

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.UtcDateTime);
        clock.UtcNowOffset.Returns(Now);

        var handler = new GetMyDashboardHandler(
            h.Db, auth, currentUser, h.Attainment, h.Sender, clock);

        var result = await handler.Handle(
            new GetMyDashboardQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Linked.Should().BeFalse();
        result.Value.Quotas.Should().BeEmpty();
    }
}
