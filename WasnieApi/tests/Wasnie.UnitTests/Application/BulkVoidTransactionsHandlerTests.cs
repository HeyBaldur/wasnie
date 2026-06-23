using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>PASO 3 — bulk void: batch, anti-double-pay (only Pending; active credits & paid rejected with reason).</summary>
public sealed class BulkVoidTransactionsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext NewDb(string name, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());
    }

    private static BulkVoidTransactionsHandler NewHandler(ApplicationDbContext db)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-1");
        return new BulkVoidTransactionsHandler(
            db, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            Substitute.For<IAuthorizationService>());
    }

    private static CompensationTransaction Tx(Guid tenantId, string reference) =>
        CompensationTransaction.Ingest(tenantId, reference, Guid.NewGuid(), Money.Of(100m, "EUR"),
            new DateOnly(2026, 6, 1), TransactionSource.Manual, "seed", Guid.NewGuid(), Now, Guid.NewGuid());

    [Fact]
    public async Task Voids_multiple_pending_transactions_in_batch()
    {
        var tenant = Guid.NewGuid();
        var db = NewDb(nameof(Voids_multiple_pending_transactions_in_batch), tenant);
        var t1 = Tx(tenant, "R1"); var t2 = Tx(tenant, "R2"); var t3 = Tx(tenant, "R3");
        db.CompensationTransactions.AddRange(t1, t2, t3);
        await db.SaveChangesAsync();

        var result = await NewHandler(db).Handle(
            new BulkVoidTransactionsCommand([t1.Id, t2.Id, t3.Id], "wrong currency import"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VoidedCount.Should().Be(3);
        result.Value.Errors.Should().BeEmpty();
        (await db.CompensationTransactions.CountAsync(t => t.Status == CompensationTransactionStatus.Cancelled))
            .Should().Be(3);
        // Audit entry per voided transaction.
        (await db.AuditLogs.CountAsync(a => a.Action == "transaction_voided")).Should().Be(3);
    }

    [Fact]
    public async Task Rejects_calculated_and_paid_with_reason_but_voids_the_pending_ones()
    {
        var tenant = Guid.NewGuid();
        var db = NewDb(nameof(Rejects_calculated_and_paid_with_reason_but_voids_the_pending_ones), tenant);
        var pending = Tx(tenant, "P1");
        var calculated = Tx(tenant, "C1");
        calculated.MarkCalculated(1, Money.Of(5m, "EUR"), "seed", Now, Guid.NewGuid());
        var paid = Tx(tenant, "PA1");
        paid.MarkCalculated(1, Money.Of(5m, "EUR"), "seed", Now, Guid.NewGuid());
        paid.MarkPaid("seed", Now, Guid.NewGuid());
        db.CompensationTransactions.AddRange(pending, calculated, paid);
        await db.SaveChangesAsync();

        var result = await NewHandler(db).Handle(
            new BulkVoidTransactionsCommand([pending.Id, calculated.Id, paid.Id], "cleanup"), default);

        result.Value!.VoidedCount.Should().Be(1);
        result.Value.Errors.Should().HaveCount(2);
        result.Value.Errors.Should().Contain(e => e.Contains("C1"));
        result.Value.Errors.Should().Contain(e => e.Contains("PA1"));
        // Anti-double-pay: the paid/calculated ones are untouched.
        (await db.CompensationTransactions.FirstAsync(t => t.ReferenceNumber == "PA1")).Status
            .Should().Be(CompensationTransactionStatus.Paid);
        (await db.CompensationTransactions.FirstAsync(t => t.ReferenceNumber == "C1")).Status
            .Should().Be(CompensationTransactionStatus.Calculated);
    }

    [Fact]
    public async Task Rejects_transaction_with_active_credit_with_reason()
    {
        var tenant = Guid.NewGuid();
        var db = NewDb(nameof(Rejects_transaction_with_active_credit_with_reason), tenant);
        var tx = Tx(tenant, "WITHCREDIT");
        db.CompensationTransactions.Add(tx);
        var ruleId = Guid.NewGuid();
        var credit = Credit.Allocate(tenant, tx.Id, tx.PayeeId!.Value, Guid.NewGuid(), ruleId,
            RuleSnapshot.Freeze(ruleId, Guid.NewGuid(), 1, "Base", RateTable.Flat(0.05m), Trigger.Always(), Now),
            Money.Of(100m, "EUR"), Money.Of(5m, "EUR"), Percentage.FromPercent(5m), CreditRole.Primary,
            "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        db.Credits.Add(credit);
        await db.SaveChangesAsync();

        var result = await NewHandler(db).Handle(
            new BulkVoidTransactionsCommand([tx.Id], "cleanup"), default);

        result.Value!.VoidedCount.Should().Be(0);
        result.Value.Errors.Should().ContainSingle().Which.Should().Contain("active credits");
        (await db.CompensationTransactions.FirstAsync()).Status.Should().Be(CompensationTransactionStatus.Pending);
    }

    [Fact]
    public async Task Reports_missing_ids_and_rejects_short_reason()
    {
        var tenant = Guid.NewGuid();
        var db = NewDb(nameof(Reports_missing_ids_and_rejects_short_reason), tenant);
        var tx = Tx(tenant, "R1");
        db.CompensationTransactions.Add(tx);
        await db.SaveChangesAsync();
        var handler = NewHandler(db);

        var missingId = Guid.NewGuid();
        var ok = await handler.Handle(new BulkVoidTransactionsCommand([tx.Id, missingId], "valid reason"), default);
        ok.Value!.VoidedCount.Should().Be(1);
        ok.Value.Errors.Should().ContainSingle().Which.Should().Contain("not found");

        var shortReason = await handler.Handle(new BulkVoidTransactionsCommand([tx.Id], "x"), default);
        shortReason.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Is_tenant_isolated()
    {
        const string dbName = nameof(Is_tenant_isolated);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbA = NewDb(dbName, tenantA);
        var txA = Tx(tenantA, "A1");
        dbA.CompensationTransactions.Add(txA);
        await dbA.SaveChangesAsync();

        // Tenant B tries to void tenant A's transaction id — the ambient filter hides it → "not found".
        var dbB = NewDb(dbName, tenantB);
        var result = await NewHandler(dbB).Handle(new BulkVoidTransactionsCommand([txA.Id], "cross tenant"), default);

        result.Value!.VoidedCount.Should().Be(0);
        result.Value.Errors.Should().ContainSingle().Which.Should().Contain("not found");
        (await dbA.CompensationTransactions.FirstAsync()).Status.Should().Be(CompensationTransactionStatus.Pending);
    }
}
