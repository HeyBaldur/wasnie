#pragma warning disable CS8602

using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Models.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.BackgroundJobs;

/// <summary>
/// Integration tests for ProcessPendingTransactionsJobHandler.
/// Uses a real Testcontainers SQL Server (same pattern as CreditAllocationServiceTests).
/// </summary>
public sealed class ProcessPendingJobTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private string _connectionString = string.Empty;

    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Currency = "EUR";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        await using var db = CreateDb(Guid.Empty);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ApplicationDbContext CreateDb(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_connectionString)
                .Options,
            new FixedTenantContext(tenantId),
            NoOpPublisher.Instance);

    private ProcessPendingTransactionsJobHandler CreateHandler(ApplicationDbContext db)
    {
        var clock = new FakeClock(Now.UtcDateTime);
        var guidGen = new FakeGuidGenerator();
        var allocationService = new CreditAllocationService(
            db, guidGen, clock, NullLogger<CreditAllocationService>.Instance);
        return new ProcessPendingTransactionsJobHandler(
            db, clock, guidGen, allocationService,
            NullLogger<ProcessPendingTransactionsJobHandler>.Instance);
    }

    private static JobContext NoOpJobContext() =>
        new(Guid.NewGuid(), new NoOpJobService());

    private static Payee MakePayee(Guid tenantId, string code) =>
        Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);

    private static Plan MakePlanWithFlatRule(Guid tenantId, Guid planId, DateOnly start, DateOnly end)
    {
        var plan = Plan.Create(tenantId, "Test Plan", "desc",
            DateRange.Of(start, end), Currency, "test", planId, Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(0.05m));
        return plan;
    }

    private static PlanAssignment MakeAssignment(Guid tenantId, Guid planId, Guid payeeId, DateOnly start, DateOnly end)
    {
        var payeeRef = PayeeReference.Snapshot(payeeId, "Test Payee", "CODE");
        return PlanAssignment.Create(tenantId, planId, payeeId, payeeRef,
            DateRange.Of(start, end), "test", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    private static CompensationTransaction MakePendingTx(Guid tenantId, Guid payeeId, string refNum, DateOnly date)
    {
        var money = Money.Of(1000m, Currency);
        return CompensationTransaction.Ingest(tenantId, refNum, payeeId, money, date,
            TransactionSource.EtlImport, "test", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ByPlanAssignment_AllPendingTransactionsGetCredits()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var planStart = new DateOnly(2026, 1, 1);
        var planEnd = new DateOnly(2026, 12, 31);

        await using var db = CreateDb(tenantId);
        var payee = MakePayee(tenantId, "EMP001");
        var actualPayeeId = payee.Id;

        var plan = MakePlanWithFlatRule(tenantId, planId, planStart, planEnd);
        var assignment = MakeAssignment(tenantId, planId, actualPayeeId, planStart, planEnd);

        var txDate = new DateOnly(2026, 3, 10);
        var transactions = Enumerable.Range(1, 5).Select(i =>
            MakePendingTx(tenantId, actualPayeeId, $"REF-{i:D3}", txDate)).ToList();

        db.Payees.Add(payee);
        db.CompensationPlans.Add(plan);
        db.PlanAssignments.Add(assignment);
        db.CompensationTransactions.AddRange(transactions);
        await db.SaveChangesAsync();

        var payload = new ProcessPendingTransactionsPayload(
            tenantId, ProcessPendingScope.ByPlanAssignment, assignment.Id,
            null, null, "test-user", "test@test.com");

        var handler = CreateHandler(db);
        await handler.HandleAsync(payload, NoOpJobContext(), CancellationToken.None);

        // All 5 transactions should now be Calculated with Credits.
        var credits = await db.Credits.Where(c => c.PayeeId == actualPayeeId).ToListAsync();
        credits.Should().HaveCount(5);

        var txIds = transactions.Select(t => t.Id).ToList();
        var processedTxs = await db.CompensationTransactions
            .Where(t => txIds.Contains(t.Id))
            .ToListAsync();
        processedTxs.Should().AllSatisfy(t =>
            t.Status.Should().Be(CompensationTransactionStatus.Calculated));
    }

    [Fact]
    public async Task HandleAsync_SkipsTransactionsWithExistingNonSupersededCredits()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var plan2Id = Guid.NewGuid();
        var planStart = new DateOnly(2026, 1, 1);
        var planEnd = new DateOnly(2026, 12, 31);

        await using var db = CreateDb(tenantId);
        var payee = MakePayee(tenantId, "EMP002");
        var actualPayeeId = payee.Id;

        var plan = MakePlanWithFlatRule(tenantId, planId, planStart, planEnd);
        var assignment = MakeAssignment(tenantId, planId, actualPayeeId, planStart, planEnd);

        // Transaction 1: Pending, will be processed.
        var tx1 = MakePendingTx(tenantId, actualPayeeId, "REF-SKIP-001", new DateOnly(2026, 3, 1));
        // Transaction 2: Pending but already has a non-superseded Credit (from plan2Id).
        var tx2 = MakePendingTx(tenantId, actualPayeeId, "REF-SKIP-002", new DateOnly(2026, 3, 1));

        db.Payees.Add(payee);
        db.CompensationPlans.Add(plan);
        db.PlanAssignments.Add(assignment);
        db.CompensationTransactions.AddRange(tx1, tx2);
        await db.SaveChangesAsync();

        // Pre-allocate a credit for tx2 from "another plan" to trigger the skipping rule.
        var existingCredit = Wasnie.Domain.Compensation.Credits.Credit.Allocate(
            tenantId, tx2.Id, actualPayeeId, plan2Id, Guid.NewGuid(),
            RuleSnapshot.Freeze(Guid.NewGuid(), plan2Id, 1, "Rule",
                RateTable.Flat(0.1m), new Trigger { Conditions = [], LogicalOperator = LogicalOperator.And },
                Now),
            Money.Of(1000m, Currency), Money.Of(100m, Currency),
            Percentage.FromPercent(100), Wasnie.Domain.Compensation.Enums.CreditRole.Primary,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());

        db.Credits.Add(existingCredit);
        await db.SaveChangesAsync();

        var payload = new ProcessPendingTransactionsPayload(
            tenantId, ProcessPendingScope.ByPlanAssignment, assignment.Id,
            null, null, "test-user", "test@test.com");

        var handler = CreateHandler(db);
        await handler.HandleAsync(payload, NoOpJobContext(), CancellationToken.None);

        // tx1 should be processed; tx2 skipped (existing credit from another plan).
        var newCreditsForTx1 = await db.Credits
            .Where(c => c.TransactionId == tx1.Id && c.PlanId == planId)
            .ToListAsync();
        newCreditsForTx1.Should().HaveCount(1);

        // tx2 should still be Pending (skipped by overlap rule).
        var tx2State = await db.CompensationTransactions.FindAsync(tx2.Id);
        tx2State!.Status.Should().Be(CompensationTransactionStatus.Pending);
    }

    [Fact]
    public async Task HandleAsync_CancellationAtChunkBoundary_StopsProcessing()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var planStart = new DateOnly(2026, 1, 1);
        var planEnd = new DateOnly(2026, 12, 31);

        await using var db = CreateDb(tenantId);
        var payee = MakePayee(tenantId, "EMP003");
        var actualPayeeId = payee.Id;

        var plan = MakePlanWithFlatRule(tenantId, planId, planStart, planEnd);
        var assignment = MakeAssignment(tenantId, planId, actualPayeeId, planStart, planEnd);

        // 3 chunks × 50 = need > 50 transactions. Use 55 (chunk1=50, chunk2=5).
        var txDate = new DateOnly(2026, 3, 5);
        var transactions = Enumerable.Range(1, 55).Select(i =>
            MakePendingTx(tenantId, actualPayeeId, $"REF-CANCEL-{i:D3}", txDate)).ToList();

        db.Payees.Add(payee);
        db.CompensationPlans.Add(plan);
        db.PlanAssignments.Add(assignment);
        db.CompensationTransactions.AddRange(transactions);
        await db.SaveChangesAsync();

        // Signal cancellation immediately — first chunk should complete, second should be aborted.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var payload = new ProcessPendingTransactionsPayload(
            tenantId, ProcessPendingScope.ByPlanAssignment, assignment.Id,
            null, null, "test-user", "test@test.com");

        var handler = CreateHandler(db);

        // The handler should throw OperationCanceledException (or derived TaskCanceledException) when
        // the token is signalled — either at a chunk boundary or during a DB operation.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await handler.HandleAsync(payload, NoOpJobContext(), cts.Token));
    }

    // ── Inner doubles ─────────────────────────────────────────────────────────

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool IsResolved => true;
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public static readonly NoOpPublisher Instance = new();
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification => Task.CompletedTask;
    }

    private sealed class NoOpJobService : IBackgroundJobService
    {
        public Task<Guid> EnqueueAsync<TPayload>(TPayload payload, Guid tenantId, string userId, string userEmail, CancellationToken ct = default)
            where TPayload : notnull => Task.FromResult(Guid.NewGuid());
        public Task<Wasnie.Application.Common.Models.JobStatusDto?> GetJobStatusAsync(Guid jobId, Guid tenantId, CancellationToken ct = default) => Task.FromResult<Wasnie.Application.Common.Models.JobStatusDto?>(null);
        public Task UpdateProgressAsync(Guid jobId, int current, int total, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkRunningAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CancelJobAsync(Guid jobId, Guid tenantId, CancellationToken ct = default) => Task.FromResult(true);
        public Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
