using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Dashboard;
using Wasnie.Application.Compensation.Queries.Dashboard;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.Builders;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The attention card's "plan can't be determined" section. The point of the grouping is that ONE
/// payee's overlapping assignments are ONE problem — a payee with 43 blocked transactions must read as
/// a single row to fix, not 43 rows to wade through.
/// </summary>
public sealed class AmbiguousAttributionCardTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly TxDate = new(2026, 5, 1);

    private static ApplicationDbContext NewDb(string name)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
    }

    private static Payee SeedPayee(ApplicationDbContext db, string name, string code)
    {
        var p = Payee.Create(TenantId, name, code, null, null, "seed", Guid.NewGuid(), new DateTimeOffset(Now));
        db.Payees.Add(p);
        db.SaveChanges();
        return p;
    }

    private static Guid SeedPlan(ApplicationDbContext db, string name, string currency = "EUR")
    {
        var plan = new PlanBuilder().WithTenantId(TenantId).WithName(name).WithCurrency(currency)
            .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Build();
        db.CompensationPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static Guid SeedAssignment(ApplicationDbContext db, Guid planId, Guid payeeId, bool active = true)
    {
        var a = PlanAssignment.Create(
            TenantId, planId, payeeId, PayeeReference.Snapshot(payeeId, "Name", "EMP"),
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            "seed", Guid.NewGuid(), new DateTimeOffset(Now), Guid.NewGuid());
        if (!active) a.Deactivate("seed", new DateTimeOffset(Now), Guid.NewGuid());
        db.PlanAssignments.Add(a);
        db.SaveChanges();
        return a.Id;
    }

    private static void SeedPendingTx(
        ApplicationDbContext db, Guid payeeId, string currency = "EUR", Guid? selected = null)
    {
        var tx = CompensationTransaction.Ingest(
            TenantId, $"REF-{Guid.NewGuid():N}", payeeId, Money.Of(100m, currency), TxDate,
            TransactionSource.CrmSync, "seed", Guid.NewGuid(), new DateTimeOffset(Now), Guid.NewGuid(),
            selectedPlanAssignmentId: selected);
        db.CompensationTransactions.Add(tx);
        db.SaveChanges();
    }

    private static GetDashboardSummaryHandler NewHandler(ApplicationDbContext db) =>
        new(db, Substitute.For<IAuthorizationService>(), new FakeClock(Now));

    // (d) One row per payee, with the transaction count and the competing plan names.
    [Fact]
    public async Task Groups_by_payee_with_the_transaction_count_and_competing_plans()
    {
        var db = NewDb(nameof(Groups_by_payee_with_the_transaction_count_and_competing_plans));
        var planA = SeedPlan(db, "Plan A");
        var planB = SeedPlan(db, "Plan B");

        var rudolph = SeedPayee(db, "Rudolph", "CEO-001");
        SeedAssignment(db, planA, rudolph.Id);
        SeedAssignment(db, planB, rudolph.Id);
        for (var i = 0; i < 3; i++) SeedPendingTx(db, rudolph.Id);

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        var rows = result.Value!.ActionBand.AmbiguousAttributionPayees;
        rows.Should().ContainSingle();               // ONE row, not three
        rows[0].PayeeName.Should().Be("Rudolph");
        rows[0].EmployeeCode.Should().Be("CEO-001");
        rows[0].TransactionCount.Should().Be(3);
        rows[0].PlanNames.Should().BeEquivalentTo(["Plan A", "Plan B"]);
    }

    [Fact]
    public async Task A_payee_with_a_single_eligible_plan_is_not_listed()
    {
        var db = NewDb(nameof(A_payee_with_a_single_eligible_plan_is_not_listed));
        var plan = SeedPlan(db, "Only Plan");
        var payee = SeedPayee(db, "Solo", "E1");
        SeedAssignment(db, plan, payee.Id);
        SeedPendingTx(db, payee.Id);

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        result.Value!.ActionBand.AmbiguousAttributionPayees.Should().BeEmpty();
    }

    // Transactions with a declared plan are resolved, so they must not inflate the card.
    [Fact]
    public async Task Transactions_with_a_declared_plan_are_excluded_from_the_count()
    {
        var db = NewDb(nameof(Transactions_with_a_declared_plan_are_excluded_from_the_count));
        var planA = SeedPlan(db, "Plan A");
        var planB = SeedPlan(db, "Plan B");
        var payee = SeedPayee(db, "Rudolph", "CEO-001");
        var assignmentA = SeedAssignment(db, planA, payee.Id);
        SeedAssignment(db, planB, payee.Id);

        SeedPendingTx(db, payee.Id);                          // ambiguous
        SeedPendingTx(db, payee.Id, selected: assignmentA);   // resolved by the admin

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        var rows = result.Value!.ActionBand.AmbiguousAttributionPayees;
        rows.Should().ContainSingle();
        rows[0].TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task Several_affected_payees_are_listed_most_blocked_first()
    {
        var db = NewDb(nameof(Several_affected_payees_are_listed_most_blocked_first));
        var planA = SeedPlan(db, "Plan A");
        var planB = SeedPlan(db, "Plan B");

        var few = SeedPayee(db, "Few", "E1");
        SeedAssignment(db, planA, few.Id);
        SeedAssignment(db, planB, few.Id);
        SeedPendingTx(db, few.Id);

        var many = SeedPayee(db, "Many", "E2");
        SeedAssignment(db, planA, many.Id);
        SeedAssignment(db, planB, many.Id);
        for (var i = 0; i < 4; i++) SeedPendingTx(db, many.Id);

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        var rows = result.Value!.ActionBand.AmbiguousAttributionPayees;
        rows.Should().HaveCount(2);
        rows[0].PayeeName.Should().Be("Many");
        rows[0].TransactionCount.Should().Be(4);
        rows[1].PayeeName.Should().Be("Few");
    }

    // Deactivating the surplus assignment is the primary fix — the card must clear afterwards.
    [Fact]
    public async Task Deactivating_the_surplus_assignment_clears_the_payee_from_the_card()
    {
        var db = NewDb(nameof(Deactivating_the_surplus_assignment_clears_the_payee_from_the_card));
        var planA = SeedPlan(db, "Plan A");
        var planB = SeedPlan(db, "Plan B");
        var payee = SeedPayee(db, "Rudolph", "CEO-001");
        SeedAssignment(db, planA, payee.Id);
        SeedAssignment(db, planB, payee.Id, active: false);   // the overlap already resolved
        SeedPendingTx(db, payee.Id);

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        result.Value!.ActionBand.AmbiguousAttributionPayees.Should().BeEmpty();
    }
}
