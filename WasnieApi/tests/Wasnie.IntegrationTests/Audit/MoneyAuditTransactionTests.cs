using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Behaviors;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Audit;
using Wasnie.IntegrationTests.Infrastructure;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Audit;

/// <summary>
/// Proves that the money-critical audit path (IMoneyCriticalCommand) is atomic:
/// audit failure rolls back the business write, and success persists both atomically.
/// Uses real Testcontainers MSSQL for genuine transactional semantics (Rule 7.3.1).
/// </summary>
[Collection(MoneyAuditCollection.Name)]
public sealed class MoneyAuditTransactionTests : IAsyncLifetime
{
    private readonly MoneyAuditTestFixture _fixture;
    private Guid _tenantId;

    public MoneyAuditTransactionTests(MoneyAuditTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _tenantId = Guid.NewGuid();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Test 1: Non-money path ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonMoneyCommand_AuditFails_BusinessSucceeds_ExceptionSwallowed()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var payeeId = Guid.NewGuid();

        var behavior = new AuditBehavior<FakeNonMoneyCommand, Result>(
            new ThrowingAuditDispatcher(),
            new FixedTenantContext(_tenantId),
            new FixedCurrentUserService(),
            db);

        var command = new FakeNonMoneyCommand { AuditResourceId = payeeId.ToString() };
        var next = CreatePayeeDelegate(db, _tenantId, payeeId);

        // Should NOT throw despite audit failure
        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("business operation must succeed when non-money audit fails");

        await using var verifyDb = _fixture.CreateDb(_tenantId);
        var persisted = await verifyDb.Payees.IgnoreQueryFilters()
            .AnyAsync(p => p.Id == payeeId);
        persisted.Should().BeTrue("business write must persist even when audit write fails");
    }

    // ── Test 2: Money-critical path — audit fails → rollback ──────────────────

    [Fact]
    public async Task Handle_MoneyCriticalCommand_AuditFails_RollsBackBusiness()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var payeeId = Guid.NewGuid();

        var behavior = new AuditBehavior<FakeMoneyCriticalCommand, Result>(
            new ThrowingAuditDispatcher(),
            new FixedTenantContext(_tenantId),
            new FixedCurrentUserService(),
            db);

        var command = new FakeMoneyCriticalCommand { AuditResourceId = payeeId.ToString() };
        var next = CreatePayeeDelegate(db, _tenantId, payeeId);

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);
        await act.Should().ThrowAsync<Exception>("money-critical audit failure must propagate");

        // Fresh context — original db is in an indeterminate state post-exception
        await using var verifyDb = _fixture.CreateDb(_tenantId);
        var persisted = await verifyDb.Payees.IgnoreQueryFilters()
            .AnyAsync(p => p.Id == payeeId);
        persisted.Should().BeFalse("business write must be rolled back when money-critical audit fails");
    }

    // ── Test 3: Money-critical path — both succeed → atomic commit ─────────────

    [Fact]
    public async Task Handle_MoneyCriticalCommand_AuditSucceeds_PersistsAtomically()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var payeeId = Guid.NewGuid();
        var clock = new FakeClock();

        // Dispatcher shares the same db instance → participates in the same transaction
        var behavior = new AuditBehavior<FakeMoneyCriticalCommand, Result>(
            new SyncAuditDispatcher(db, clock),
            new FixedTenantContext(_tenantId),
            new FixedCurrentUserService(),
            db);

        var command = new FakeMoneyCriticalCommand { AuditResourceId = payeeId.ToString() };
        var next = CreatePayeeDelegate(db, _tenantId, payeeId);

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verifyDb = _fixture.CreateDb(_tenantId);

        var payeePersisted = await verifyDb.Payees.IgnoreQueryFilters()
            .AnyAsync(p => p.Id == payeeId);
        payeePersisted.Should().BeTrue("business write must persist after atomic commit");

        var auditPersisted = await verifyDb.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(l => l.ResourceId == payeeId.ToString() && l.TenantId == _tenantId);
        auditPersisted.Should().BeTrue("audit log must persist atomically with business write");
    }

    // ── KAN-34: a failed handler must not leave a row saying it succeeded ─────

    /// <summary>
    /// Edge 1, non-money path. Before KAN-34 the behavior dispatched the entry straight after
    /// next() without ever looking at the Result, so a handler that returned Result.Failure —
    /// without throwing — left a row indistinguishable from a success.
    /// </summary>
    [Fact]
    public async Task Handle_NonMoneyCommand_HandlerReturnsFailure_WritesNoAuditRow()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var payeeId = Guid.NewGuid();
        var dispatcher = new RecordingAuditDispatcher();

        var behavior = new AuditBehavior<FakeNonMoneyCommand, Result>(
            dispatcher, new FixedTenantContext(_tenantId), new FixedCurrentUserService(), db);

        var command = new FakeNonMoneyCommand { AuditResourceId = payeeId.ToString() };

        var result = await behavior.Handle(
            command, () => Task.FromResult(Result.Failure("Plan not found.")), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        dispatcher.Entries.Should().BeEmpty("a handler that did nothing must not be recorded as having acted");
    }

    /// <summary>
    /// Edge 1, MONEY path — the half the ticket assumed was already safe. The transaction defends
    /// against the AUDIT WRITE failing; it says nothing about the handler's Result, which travels
    /// back as a return value and commits happily. Four failed attempts at reverting one 2.980 EUR
    /// commission each left a row claiming it had been reverted.
    ///
    /// It also pins the commit-on-failure decision: a write the handler deliberately persisted on
    /// its way to Failure ("ingest and mark", Rule B2) SURVIVES. Only the audit row is suppressed.
    /// </summary>
    [Fact]
    public async Task Handle_MoneyCriticalCommand_HandlerReturnsFailure_WritesNoAuditRow_ButKeepsBusinessWrite()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var payeeId = Guid.NewGuid();
        var clock = new FakeClock();

        var behavior = new AuditBehavior<FakeMoneyCriticalCommand, Result>(
            new SyncAuditDispatcher(db, clock),
            new FixedTenantContext(_tenantId),
            new FixedCurrentUserService(),
            db);

        var command = new FakeMoneyCriticalCommand { AuditResourceId = payeeId.ToString() };
        var succeedingWrite = CreatePayeeDelegate(db, _tenantId, payeeId);

        var result = await behavior.Handle(
            command,
            async () =>
            {
                await succeedingWrite();
                return Result.Failure("Transaction not found.");
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();

        await using var verifyDb = _fixture.CreateDb(_tenantId);

        var auditPersisted = await verifyDb.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(l => l.ResourceId == payeeId.ToString() && l.TenantId == _tenantId);
        auditPersisted.Should().BeFalse("a money command that failed must not claim it happened");

        var payeePersisted = await verifyDb.Payees.IgnoreQueryFilters().AnyAsync(p => p.Id == payeeId);
        payeePersisted.Should().BeTrue("the transaction still commits: a deliberate write on the failure path survives");
    }

    /// <summary>
    /// Happy path — exactly one row on success. Guards the fix against overshooting into
    /// suppressing legitimate entries.
    /// </summary>
    [Fact]
    public async Task Handle_NonMoneyCommand_HandlerReturnsSuccess_WritesExactlyOneAuditRow()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var payeeId = Guid.NewGuid();
        var dispatcher = new RecordingAuditDispatcher();

        var behavior = new AuditBehavior<FakeNonMoneyCommand, Result>(
            dispatcher, new FixedTenantContext(_tenantId), new FixedCurrentUserService(), db);

        var command = new FakeNonMoneyCommand { AuditResourceId = payeeId.ToString() };

        var result = await behavior.Handle(command, CreatePayeeDelegate(db, _tenantId, payeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dispatcher.Entries.Should().ContainSingle();
        dispatcher.Entries[0].Action.Should().Be(AuditActions.PayeeCreated);
        dispatcher.Entries[0].ResourceId.Should().Be(payeeId.ToString());
        dispatcher.Entries[0].ActorUserId.Should().Be("test-user");
    }

    /// <summary>
    /// Edge 2 — an exception leaves no row and is not swallowed. The catch around DispatchAsync
    /// protects the user operation from an audit failure; it must never absorb the handler's own.
    /// </summary>
    [Fact]
    public async Task Handle_NonMoneyCommand_HandlerThrows_WritesNoAuditRow_AndPropagates()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var dispatcher = new RecordingAuditDispatcher();

        var behavior = new AuditBehavior<FakeNonMoneyCommand, Result>(
            dispatcher, new FixedTenantContext(_tenantId), new FixedCurrentUserService(), db);

        var command = new FakeNonMoneyCommand { AuditResourceId = Guid.NewGuid().ToString() };

        var act = async () => await behavior.Handle(
            command,
            () => throw new InvalidOperationException("handler blew up"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("handler blew up");
        dispatcher.Entries.Should().BeEmpty();
    }

    /// <summary>
    /// A response that is not a Result must keep being audited exactly as before: several auditable
    /// commands return a DTO or Unit and signal failure by throwing. The fix must not silence them.
    /// </summary>
    [Fact]
    public async Task Handle_NonResultResponse_StillWritesAuditRow()
    {
        await using var db = _fixture.CreateDb(_tenantId);
        var dispatcher = new RecordingAuditDispatcher();

        var behavior = new AuditBehavior<FakeNonMoneyCommand, string>(
            dispatcher, new FixedTenantContext(_tenantId), new FixedCurrentUserService(), db);

        var command = new FakeNonMoneyCommand { AuditResourceId = Guid.NewGuid().ToString() };

        var response = await behavior.Handle(command, () => Task.FromResult("done"), CancellationToken.None);

        response.Should().Be("done");
        dispatcher.Entries.Should().ContainSingle("a non-Result response carries no failure signal to honour");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RequestHandlerDelegate<Result> CreatePayeeDelegate(
        ApplicationDbContext db,
        Guid tenantId,
        Guid payeeId)
    {
        return async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var payee = Payee.Create(
                tenantId,
                fullName: "Money Audit Test Payee",
                employeeCode: payeeId.ToString("N")[..12],
                email: $"audit-{payeeId:N}@test.com",
                hireDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                createdBy: "test-user",
                id: payeeId,
                now: now);

            db.Payees.Add(payee);
            await db.SaveChangesAsync(CancellationToken.None);
            return Result.Success();
        };
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class RecordingAuditDispatcher : IAuditDispatcher
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task DispatchAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAuditDispatcher : IAuditDispatcher
    {
        public Task DispatchAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit write failure.");
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? Email => "test@example.com";
        public bool IsAuthenticated => true;
    }

    // ── Test commands (test-only; NOT in production code) ────────────────────

    // Extension point: when Phase 2 Transaction commands are added, they implement
    // IMoneyCriticalCommand directly. This test-only command proves the mechanism works.

    private sealed record FakeNonMoneyCommand : IAuditableCommand
    {
        public string AuditAction => AuditActions.PayeeCreated;
        public string AuditResourceType => ResourceTypes.Payee;
        public string? AuditResourceId { get; init; }
        public string? AuditDisplayName => "Test Payee";
    }

    private sealed record FakeMoneyCriticalCommand : IMoneyCriticalCommand
    {
        public string AuditAction => AuditActions.PayeeCreated;
        public string AuditResourceType => ResourceTypes.Payee;
        public string? AuditResourceId { get; init; }
        public string? AuditDisplayName => "Test Money Payee";
    }
}
