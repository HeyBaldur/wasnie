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
/// Tests the dashboard's "Transactions that need attention" computation (BuildUnprocessablePendingAsync):
/// each Pending transaction is classified into exactly one primary reason, and processable ones are excluded.
/// </summary>
public sealed class UnprocessablePendingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewDb(string name)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
    }

    private static Guid SeedPayee(ApplicationDbContext db, string code)
    {
        var p = Payee.Create(TenantId, $"Payee {code}", code, null, null, "seed", Guid.NewGuid(), new DateTimeOffset(Now));
        db.Payees.Add(p);
        db.SaveChanges();
        return p.Id;
    }

    private static Guid SeedPlan(ApplicationDbContext db, string name, string currency)
    {
        var plan = new PlanBuilder().WithTenantId(TenantId).WithName(name).WithCurrency(currency)
            .WithPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Build();
        db.CompensationPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static void SeedAssignment(
        ApplicationDbContext db, Guid planId, Guid payeeId, bool active,
        DateOnly start, DateOnly end)
    {
        var a = PlanAssignment.Create(
            TenantId, planId, payeeId, PayeeReference.Snapshot(payeeId, "Name", "EMP"),
            DateRange.Of(start, end), "seed", Guid.NewGuid(), new DateTimeOffset(Now), Guid.NewGuid());
        if (!active) a.Deactivate("seed", new DateTimeOffset(Now), Guid.NewGuid());
        db.PlanAssignments.Add(a);
        db.SaveChanges();
    }

    private static void SeedPendingTx(ApplicationDbContext db, Guid? payeeId, string currency, DateOnly date)
    {
        var tx = CompensationTransaction.Ingest(
            TenantId, $"REF-{Guid.NewGuid():N}", payeeId, Money.Of(100m, currency), date,
            TransactionSource.CrmSync, "seed", Guid.NewGuid(), new DateTimeOffset(Now), Guid.NewGuid());
        db.CompensationTransactions.Add(tx);
        db.SaveChanges();
    }

    private static GetDashboardSummaryHandler NewHandler(ApplicationDbContext db)
    {
        var authz = Substitute.For<IAuthorizationService>();
        return new GetDashboardSummaryHandler(db, authz, new FakeClock(Now));
    }

    [Fact]
    public async Task Classifies_each_pending_transaction_into_one_primary_reason()
    {
        var db = NewDb(nameof(Classifies_each_pending_transaction_into_one_primary_reason));
        var eurPlan = SeedPlan(db, "EU Plan", "EUR");

        // Payee with an ACTIVE EUR assignment covering Q2.
        var p1 = SeedPayee(db, "E1");
        SeedAssignment(db, eurPlan, p1, active: true, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));
        SeedPendingTx(db, p1, "USD", new DateOnly(2026, 5, 1));  // covered, but USD≠EUR → CurrencyMismatch
        SeedPendingTx(db, p1, "EUR", new DateOnly(2026, 5, 2));  // covered + EUR == EUR → processable (excluded)

        // Payee whose only assignment is DEACTIVATED → NoActiveAssignment.
        var p2 = SeedPayee(db, "E2");
        SeedAssignment(db, eurPlan, p2, active: false, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        SeedPendingTx(db, p2, "EUR", new DateOnly(2026, 5, 3));

        // Unassigned transaction → NoPayee.
        SeedPendingTx(db, null, "USD", new DateOnly(2026, 5, 4));

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value!.ActionBand.UnprocessablePendingItems
            .ToDictionary(i => i.Reason, i => i);

        items["NoPayee"].Count.Should().Be(1);
        items["CurrencyMismatch"].Count.Should().Be(1);
        items["CurrencyMismatch"].Currencies.Should().BeEquivalentTo(["USD"]);
        items["NoActiveAssignment"].Count.Should().Be(1);
    }

    [Fact]
    public async Task Excludes_processable_transactions_entirely()
    {
        var db = NewDb(nameof(Excludes_processable_transactions_entirely));
        var eurPlan = SeedPlan(db, "EU Plan", "EUR");
        var p1 = SeedPayee(db, "E1");
        SeedAssignment(db, eurPlan, p1, active: true, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        SeedPendingTx(db, p1, "EUR", new DateOnly(2026, 5, 1));  // fully processable

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActionBand.UnprocessablePendingItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Transaction_outside_assignment_date_range_is_no_active_assignment()
    {
        var db = NewDb(nameof(Transaction_outside_assignment_date_range_is_no_active_assignment));
        var eurPlan = SeedPlan(db, "EU Plan", "EUR");
        var p1 = SeedPayee(db, "E1");
        // Active assignment covers only Q2; the transaction is in Q3 → not covered → NoActiveAssignment.
        SeedAssignment(db, eurPlan, p1, active: true, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));
        SeedPendingTx(db, p1, "EUR", new DateOnly(2026, 8, 1));

        var result = await NewHandler(db).Handle(new GetDashboardSummaryQuery("all-time"), default);

        var items = result.Value!.ActionBand.UnprocessablePendingItems.ToDictionary(i => i.Reason, i => i);
        items.Should().ContainKey("NoActiveAssignment");
        items["NoActiveAssignment"].Count.Should().Be(1);
        items.Should().NotContainKey("CurrencyMismatch");
    }
}
