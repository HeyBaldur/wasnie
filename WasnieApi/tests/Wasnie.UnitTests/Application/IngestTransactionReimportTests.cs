using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// PASO 4 — MANUAL source uses the same centralized rule: reference that exists only as a Void can be
/// re-created (Opción B); an active reference is rejected (no 500); the void stays as history.
/// </summary>
public sealed class IngestTransactionReimportTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (ApplicationDbContext Db, IngestTransactionHandler Handler) Build(string dbName)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-1");
        var fieldReq = Substitute.For<IFieldRequirementService>();
        fieldReq.IsRequiredAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var creditAlloc = Substitute.For<Wasnie.Application.Compensation.Calculation.ICreditAllocationService>();
        creditAlloc.AllocateAsync(Arg.Any<CompensationTransaction>(), Arg.Any<CancellationToken>())
            .Returns(new List<Credit>());

        var handler = new IngestTransactionHandler(
            db, tenantCtx, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            Substitute.For<IAuthorizationService>(), fieldReq, creditAlloc, new TransactionCreateGuard(db));
        return (db, handler);
    }

    private static IngestTransactionCommand Cmd(string reference) =>
        new(reference, null, 100m, "EUR", new DateOnly(2026, 6, 1), Quantity: 1, ProcessImmediately: false);

    [Fact]
    public async Task Creates_when_reference_is_new()
    {
        var (db, handler) = Build(nameof(Creates_when_reference_is_new));
        var result = await handler.Handle(Cmd("REF-1"), default);
        result.IsSuccess.Should().BeTrue();
        (await db.CompensationTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Rejects_when_an_active_transaction_has_the_same_reference()
    {
        var (db, handler) = Build(nameof(Rejects_when_an_active_transaction_has_the_same_reference));
        (await handler.Handle(Cmd("REF-1"), default)).IsSuccess.Should().BeTrue();

        var result = await handler.Handle(Cmd("REF-1"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
        (await db.CompensationTransactions.CountAsync()).Should().Be(1); // no duplicate, no 500
    }

    [Fact]
    public async Task Allows_create_when_the_reference_only_exists_as_a_void()
    {
        var (db, handler) = Build(nameof(Allows_create_when_the_reference_only_exists_as_a_void));
        // Seed a voided transaction with the target reference.
        var voided = CompensationTransaction.Ingest(TenantId, "REF-1", null, Money.Of(50m, "USD"),
            new DateOnly(2026, 5, 1), TransactionSource.Manual, "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        voided.Cancel("wrong currency", "seed", Now, Guid.NewGuid());
        db.CompensationTransactions.Add(voided);
        await db.SaveChangesAsync();

        var result = await handler.Handle(Cmd("REF-1"), default);

        result.IsSuccess.Should().BeTrue();
        (await db.CompensationTransactions.CountAsync()).Should().Be(2); // void kept as history + new active
        (await db.CompensationTransactions.CountAsync(t => t.Status == CompensationTransactionStatus.Cancelled))
            .Should().Be(1);
        (await db.CompensationTransactions.CountAsync(t => t.Status == CompensationTransactionStatus.Pending))
            .Should().Be(1);
    }
}
