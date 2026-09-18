using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Features.Users.Handlers;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Enums;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-94: a sale the engine cannot pay on is no longer invisible to the person who made it.
///
/// ★★ THE DEFECT, IN ONE SENTENCE. Every figure on the personal dashboard is built from payouts; a
/// Pending transaction that cannot be processed produces no credit and therefore no payout. So a payee
/// with a €5,000 sale to their name and no plan assignment opened a page of zeros and could only
/// conclude the product was broken or that they had sold nothing. Both wrong, neither actionable. The
/// administrator had been able to see that row all along.
///
/// ★★ THESE TESTS DRIVE THE REAL HANDLER, NOT THE SPEC IT CALLS. Asserting on
/// <c>UnprocessablePendingSpec</c> directly would pass just as happily with the handler never calling
/// it, or calling it without the payee filter — which is precisely the interesting way to get this
/// wrong, because it would report OTHER people's stuck sales to this reader.
/// </summary>
public sealed class MyDashboardAwaitingSetupTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly SaleDate = new(2026, 9, 17);

    private const string MyUser = "user-mine";

    private sealed record Harness(
        ApplicationDbContext Db, GetMyDashboardHandler Handler, Guid TenantId, Guid MyPayeeId);

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

        // ★★ THE LEDGER SUMMARY IS STUBBED, AND WITH THE ANSWER IT REALLY GIVES HERE: an EMPTY one.
        // Every payee in these tests has stuck sales and therefore no credits, no payouts and no
        // balance — which is the whole point of the bug — so an empty summary is not a convenience,
        // it is the true state. An unconfigured substitute returns null and the handler NREs on
        // `summary.IsSuccess`, which is how this was caught.
        //
        // ★ AND IT IS STUBBED AT ALL because what is under test is the COUNT. Letting the real money
        // query run would drag in payouts, credits and the crossing they do, and a failure there would
        // read as a failure here.
        var sender = Substitute.For<ISender>();
        sender
            .Send(Arg.Any<IRequest<Result<PayeeLedgerSummaryDto>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<PayeeLedgerSummaryDto>.Success(new PayeeLedgerSummaryDto(
                mine.Id, mine.FullName, "all-time", null, null, [])));

        return new Harness(
            db,
            new GetMyDashboardHandler(
                db, auth, currentUser, Substitute.For<IQuotaAttainmentService>(), sender, clock),
            tenantId,
            mine.Id);
    }

    /// <summary>
    /// A Pending sale for a payee. Nothing else about it matters to the count.
    ///
    /// ★ Ingested, not "created": a transaction enters this system as a fact that already happened
    /// (§B2), and Pending is the status it lands in until the engine can price it.
    /// </summary>
    private static void AddPendingSale(Harness h, Guid payeeId, string currency = "EUR")
    {
        var tx = CompensationTransaction.Ingest(
            h.TenantId,
            $"REF-{Guid.NewGuid():N}"[..12],
            payeeId,
            Money.Of(5000m, currency),
            SaleDate,
            TransactionSource.Manual,
            "test",
            Guid.NewGuid(),
            Now,
            Guid.NewGuid());

        h.Db.CompensationTransactions.Add(tx);
        h.Db.SaveChanges();
    }

    /// <summary>
    /// An Active assignment covering the sale date, on a plan in the given currency.
    ///
    /// ★ THE PERIOD DELIBERATELY SPANS THE WHOLE YEAR, so no test here can pass or fail for the
    /// uninteresting reason that the sale fell a day outside it. What is under test is the currency
    /// and the existence of the assignment, not date arithmetic.
    /// </summary>
    private static void AddAssignment(Harness h, Guid payeeId, string planCurrency)
    {
        var year = DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var plan = Plan.Create(
            h.TenantId, $"Plan {planCurrency}", "test plan", year, planCurrency,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        h.Db.CompensationPlans.Add(plan);

        var assignment = PlanAssignment.Create(
            h.TenantId, plan.Id, payeeId,
            PayeeReference.Snapshot(payeeId, "Ana Garcia", "EMP-MINE"),
            year, "test", Guid.NewGuid(), Now, Guid.NewGuid());

        h.Db.PlanAssignments.Add(assignment);
        h.Db.SaveChanges();
    }

    private static async Task<int> AwaitingSetupAsync(Harness h)
    {
        var result = await h.Handler.Handle(new GetMyDashboardQuery(), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value.SalesAwaitingSetup;
    }

    /// <summary>★★ The exact shape of the reported bug: a sale, a linked payee, and no plan at all.</summary>
    [Fact]
    public async Task A_sale_with_no_plan_assignment_is_reported()
    {
        var h = Seed(nameof(A_sale_with_no_plan_assignment_is_reported));
        AddPendingSale(h, h.MyPayeeId);

        (await AwaitingSetupAsync(h)).Should().Be(1);
    }

    /// <summary>
    /// ★ THE OTHER REASON THE ENGINE REFUSES. An assignment exists and covers the date, but its plan
    /// pays in another currency — so the sale is just as stuck, and just as invisible.
    /// </summary>
    [Fact]
    public async Task A_sale_whose_currency_no_plan_pays_in_is_reported()
    {
        var h = Seed(nameof(A_sale_whose_currency_no_plan_pays_in_is_reported));
        AddAssignment(h, h.MyPayeeId, planCurrency: "PLN");
        AddPendingSale(h, h.MyPayeeId, currency: "EUR");

        (await AwaitingSetupAsync(h)).Should().Be(1);
    }

    /// <summary>
    /// ★★ NOTHING TO REPORT IS ZERO, AND THE NOTICE DISAPPEARS. A payee whose plan is set up correctly
    /// must not be warned that their pay is stuck — a notice that is always there is a notice nobody
    /// reads the day it matters.
    /// </summary>
    [Fact]
    public async Task A_sale_a_plan_covers_is_not_reported()
    {
        var h = Seed(nameof(A_sale_a_plan_covers_is_not_reported));
        AddAssignment(h, h.MyPayeeId, planCurrency: "EUR");
        AddPendingSale(h, h.MyPayeeId, currency: "EUR");

        (await AwaitingSetupAsync(h)).Should().Be(0);
    }

    [Fact]
    public async Task A_payee_with_no_sales_at_all_reports_zero()
    {
        var h = Seed(nameof(A_payee_with_no_sales_at_all_reports_zero));

        (await AwaitingSetupAsync(h)).Should().Be(0);
    }

    /// <summary>
    /// ★★ THE INTERESTING WAY TO GET THIS WRONG. Counting the tenant's stuck sales instead of this
    /// reader's would tell somebody their pay is blocked because a COLLEAGUE'S sale is — and would leak
    /// the size of the problem across the company to a Rep. The payee filter is the whole of the
    /// difference, and only a second payee in the same workspace can prove it is applied.
    /// </summary>
    [Fact]
    public async Task Somebody_elses_stuck_sale_is_not_counted_as_mine()
    {
        var h = Seed(nameof(Somebody_elses_stuck_sale_is_not_counted_as_mine));

        var other = Payee.Create(h.TenantId, "Bruno Silva", "EMP-OTHER", "bruno@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        h.Db.Payees.Add(other);
        h.Db.SaveChanges();

        AddPendingSale(h, other.Id);
        AddPendingSale(h, other.Id);

        (await AwaitingSetupAsync(h)).Should().Be(0);
    }

    /// <summary>
    /// ★ TWO STUCK SALES ARE TWO, not one and not four. The handler sums the counts of two separate
    /// spec queries, so a row matching neither or both would show up here as an off-by-N.
    /// </summary>
    [Fact]
    public async Task Several_stuck_sales_are_counted_once_each()
    {
        var h = Seed(nameof(Several_stuck_sales_are_counted_once_each));
        AddPendingSale(h, h.MyPayeeId);
        AddPendingSale(h, h.MyPayeeId);
        AddPendingSale(h, h.MyPayeeId);

        (await AwaitingSetupAsync(h)).Should().Be(3);
    }
}
