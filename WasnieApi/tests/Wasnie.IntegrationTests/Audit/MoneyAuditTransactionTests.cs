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
