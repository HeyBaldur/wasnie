using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Credits;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Handlers.Credits;
using Wasnie.Application.Models.Calculation;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// F-2: RecalculateCreditsHandler supersedes stale credits and reverts transactions to Pending.
/// Uses the same Testcontainers SQL Server fixture as CreditAllocationServiceTests.
/// </summary>
[Collection(CreditAllocationServiceCollection.Name)]
public sealed class RecalculateCreditsHandlerTests(CreditAllocationServiceFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Currency = "EUR";
    private static readonly DateOnly Q2Start = new(2026, 4, 1);
    private static readonly DateOnly Q2End = new(2026, 6, 30);

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class AlwaysAuthorized : Wasnie.Application.Common.Interfaces.IAuthorizationService
    {
        public Task RequireAsync(string permission, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestCurrentUser : Wasnie.Application.Common.Interfaces.ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? Email => "test@test.com";
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingJobService : Wasnie.Application.Common.Interfaces.IBackgroundJobService
    {
        public List<ProcessPendingTransactionsPayload> EnqueuedPayloads { get; } = [];

        public Task<Guid> EnqueueAsync<TPayload>(TPayload payload, Guid tenantId, string userId, string userEmail,
            CancellationToken ct = default) where TPayload : notnull
        {
            if (payload is ProcessPendingTransactionsPayload p) EnqueuedPayloads.Add(p);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<Wasnie.Application.Common.Models.JobStatusDto?> GetJobStatusAsync(Guid jobId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<Wasnie.Application.Common.Models.JobStatusDto?>(null);
        public Task UpdateProgressAsync(Guid jobId, int current, int total, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkRunningAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CancelJobAsync(Guid jobId, Guid tenantId, CancellationToken ct = default) => Task.FromResult(false);
        public Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetResultSummaryAsync(Guid jobId, string summaryJson, CancellationToken ct = default) => Task.CompletedTask;
    }

    // ── ISender stubs ─────────────────────────────────────────────────────────

    /// <summary>Used in tests that never seed pay runs — sender must not be called.</summary>
    private sealed class NeverCalledSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"ISender.Send was unexpectedly called with {request.GetType().Name}");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new InvalidOperationException($"ISender.Send was unexpectedly called with {typeof(TRequest).Name}");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("ISender.Send(object) unexpectedly called");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Handles DeletePayRunDraftCommand inline using the supplied db context.
    /// Follows the same cascade-delete order as DeletePayRunDraftHandler (payouts first, then run).
    /// </summary>
    private sealed class DirectDeletePayRunSender(ApplicationDbContext db) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is DeletePayRunDraftCommand cmd)
            {
                var payRun = await db.PayRuns.FindAsync([cmd.PayRunId], cancellationToken);
                if (payRun is not null)
                {
                    var payouts = await db.CompensationPayouts
                        .Where(p => p.PayRunId == cmd.PayRunId)
                        .ToListAsync(cancellationToken);
                    db.CompensationPayouts.RemoveRange(payouts);
                    db.PayRuns.Remove(payRun);
                    await db.SaveChangesAsync(cancellationToken);
                }
                return (TResponse)(object)Result.Success();
            }
            throw new NotSupportedException($"Unexpected command: {request.GetType().Name}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException($"Unexpected fire-and-forget command: {typeof(TRequest).Name}");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Payee MakePayee(Guid tenantId, Guid payeeId) =>
        Payee.Create(tenantId, "Test Payee", "EMP-001", "test@test.com",
            new DateOnly(2020, 1, 1), "test-user", payeeId, Now);

    private static Plan MakePlanFlat(Guid tenantId, Guid planId, decimal rate)
    {
        var plan = Plan.Create(tenantId, "Test Plan", "desc",
            DateRange.Of(Q2Start, Q2End), Currency, "test-user", planId, Now, Guid.NewGuid());
        plan.AddRule("Base Commission", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(rate));
        return plan;
    }

    private static PlanAssignment MakeAssignment(Guid tenantId, Guid planId, Guid payeeId)
    {
        var payeeRef = PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-001");
        return PlanAssignment.Create(tenantId, planId, payeeId, payeeRef,
            DateRange.Of(Q2Start, Q2End), "test-user", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    private RecalculateCreditsHandler CreateHandler(
        ApplicationDbContext db,
        Guid tenantId,
        RecordingJobService jobSvc,
        ISender? sender = null)
    {
        return new RecalculateCreditsHandler(
            db,
            new CreditAllocationServiceFixture.FixedTenantContext(tenantId),
            new TestCurrentUser(),
            new AlwaysAuthorized(),
            jobSvc,
            sender ?? new NeverCalledSender(),
            NullLogger<RecalculateCreditsHandler>.Instance);
    }

    private CreditAllocationService CreateAllocator(Wasnie.Infrastructure.Persistence.ApplicationDbContext db) =>
        new(db, new FakeGuidGenerator(), new FakeClock(Now.UtcDateTime),
            NullLogger<CreditAllocationService>.Instance,
            new StubQuotaAttainmentService());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SupersedesCreditsAndRevertsTransactionsToPending()
    {
        // The critical recalculation scenario:
        // Two Calculated transactions in Q2 with credits at 4% (stale, splitAtQuota was false).
        // Handler must supersede those credits and revert transactions to Pending so
        // ProcessPendingTransactionsJob can regenerate them with the correct rate.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        Guid tx1Id, tx2Id;

        // Seed plan + payee + assignment
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlanFlat(tenantId, planId, rate: 0.04m));
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId));
            await db.SaveChangesAsync();
        }

        // Allocate credits for both transactions at 4%, MarkCalculated
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx1 = CompensationTransaction.Ingest(
                tenantId, "TX-APR-1", payeeId, Money.Of(100_000m, Currency),
                new DateOnly(2026, 4, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            var tx2 = CompensationTransaction.Ingest(
                tenantId, "TX-JUN-1", payeeId, Money.Of(50_000m, Currency),
                new DateOnly(2026, 6, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());

            db.CompensationTransactions.AddRange(tx1, tx2);
            await db.SaveChangesAsync();

            var alloc = CreateAllocator(db);

            var credits1 = await alloc.AllocateAsync(tx1);
            credits1.Should().HaveCount(1); // 100k × 4% = 4,000
            foreach (var c in credits1) db.Credits.Add(c);
            tx1.MarkCalculated(1, credits1[0].CreditedAmount, "engine", Now, Guid.NewGuid());

            var credits2 = await alloc.AllocateAsync(tx2);
            credits2.Should().HaveCount(1); // 50k × 4% = 2,000
            foreach (var c in credits2) db.Credits.Add(c);
            tx2.MarkCalculated(1, credits2[0].CreditedAmount, "engine", Now, Guid.NewGuid());

            await db.SaveChangesAsync();
            tx1Id = tx1.Id;
            tx2Id = tx2.Id;
        }

        // Invoke the handler
        var jobSvc = new RecordingJobService();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId, jobSvc);
            var command = new RecalculateCreditsCommand(Q2Start, Q2End, PayeeId: null);
            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.SupersededCount.Should().Be(2, "one credit per transaction");
            result.Value.SkippedPaidCount.Should().Be(0);
            result.Value.JobIds.Should().HaveCount(1, "one job per affected payee");
        }

        // Verify DB state
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx1Reloaded = await db.CompensationTransactions.FindAsync(tx1Id);
            var tx2Reloaded = await db.CompensationTransactions.FindAsync(tx2Id);
            tx1Reloaded!.Status.Should().Be(CompensationTransactionStatus.Pending, "reverted for recalculation");
            tx2Reloaded!.Status.Should().Be(CompensationTransactionStatus.Pending, "reverted for recalculation");

            var allCredits = await db.Credits
                .Where(c => c.TenantId == tenantId)
                .ToListAsync();
            allCredits.Should().AllSatisfy(c => c.SupersededAt.Should().NotBeNull("all stale credits superseded"));
        }

        // Verify job was enqueued for the payee
        jobSvc.EnqueuedPayloads.Should().HaveCount(1);
        jobSvc.EnqueuedPayloads[0].Scope.Should().Be(ProcessPendingScope.ByPayeeAndPeriod);
        jobSvc.EnqueuedPayloads[0].ScopeId.Should().Be(payeeId);
        jobSvc.EnqueuedPayloads[0].PeriodStart.Should().Be(Q2Start);
        jobSvc.EnqueuedPayloads[0].PeriodEnd.Should().Be(Q2End);
    }

    [Fact]
    public async Task Handle_SkipsConsumedCreditsAndLeavesTransactionUntouched()
    {
        // Anti-double-pay guard: a credit with ConsumedAt != null belongs to a Paid payout.
        // RecalculateCreditsHandler must not touch consumed credits or their transactions.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlanFlat(tenantId, planId, rate: 0.04m));
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "TX-CONSUMED", payeeId, Money.Of(10_000m, Currency),
                new DateOnly(2026, 4, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var alloc = CreateAllocator(db);
            var credits = await alloc.AllocateAsync(tx);
            credits.Should().HaveCount(1);
            foreach (var c in credits) db.Credits.Add(c);
            tx.MarkCalculated(1, credits[0].CreditedAmount, "engine", Now, Guid.NewGuid());

            // Simulate the credit being consumed by a paid payout
            credits[0].Consume(Guid.NewGuid(), Now, Guid.NewGuid());

            await db.SaveChangesAsync();
            txId = tx.Id;
        }

        var jobSvc = new RecordingJobService();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId, jobSvc);
            var result = await handler.Handle(
                new RecalculateCreditsCommand(Q2Start, Q2End, PayeeId: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.SupersededCount.Should().Be(0, "consumed credit is excluded from recalculation");
            result.Value.JobIds.Should().BeEmpty("no pending transactions to reprocess");
        }

        // Transaction must remain Calculated (it was not reverted — its credit is consumed)
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var txReloaded = await db.CompensationTransactions.FindAsync(txId);
            txReloaded!.Status.Should().Be(CompensationTransactionStatus.Calculated,
                "Paid transaction's credit is consumed — handler must not revert it");

            var credit = await db.Credits.FirstAsync(c => c.TransactionId == txId);
            credit.SupersededAt.Should().BeNull("consumed credit is excluded from recalculation");
        }

        jobSvc.EnqueuedPayloads.Should().BeEmpty("no jobs enqueued when no payee transactions were reverted");
    }

    [Fact]
    public async Task Handle_SkipsPaidTransactionEvenIfCreditIsNotConsumed()
    {
        // Edge-case: transaction is Paid but its credit was not consumed (broken state).
        // The handler's explicit Paid-status check must prevent superseding the credit.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlanFlat(tenantId, planId, rate: 0.04m));
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "TX-PAID-NOCON", payeeId, Money.Of(10_000m, Currency),
                new DateOnly(2026, 4, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var alloc = CreateAllocator(db);
            var credits = await alloc.AllocateAsync(tx);
            foreach (var c in credits) db.Credits.Add(c);
            tx.MarkCalculated(1, credits[0].CreditedAmount, "engine", Now, Guid.NewGuid());
            tx.MarkPaid("system", Now, Guid.NewGuid());
            // Credit NOT consumed — simulates broken state where consume step was missed.
            await db.SaveChangesAsync();
            txId = tx.Id;
        }

        var jobSvc = new RecordingJobService();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId, jobSvc);
            var result = await handler.Handle(
                new RecalculateCreditsCommand(Q2Start, Q2End, PayeeId: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.SupersededCount.Should().Be(0, "Paid transaction's credit must not be superseded");
            result.Value.SkippedPaidCount.Should().Be(1);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var txReloaded = await db.CompensationTransactions.FindAsync(txId);
            txReloaded!.Status.Should().Be(CompensationTransactionStatus.Paid, "Paid transaction must not be reverted");

            var credit = await db.Credits.FirstAsync(c => c.TransactionId == txId);
            credit.SupersededAt.Should().BeNull("Paid transaction's credit must not be superseded");
        }

        jobSvc.EnqueuedPayloads.Should().BeEmpty();
    }

    // ── FASE 4: pay run detection and blocking ────────────────────────────────

    [Fact]
    public async Task Handle_AutoDeletesDraftPayRun_WhenAffectedPayeesHaveDraftRun()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid payRunId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlanFlat(tenantId, planId, rate: 0.04m));
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "TX-DRAFT-PR", payeeId, Money.Of(50_000m, Currency),
                new DateOnly(2026, 5, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var alloc = CreateAllocator(db);
            var credits = await alloc.AllocateAsync(tx);
            foreach (var c in credits) db.Credits.Add(c);
            tx.MarkCalculated(1, credits[0].CreditedAmount, "engine", Now, Guid.NewGuid());
            await db.SaveChangesAsync();

            // Create a Draft pay run covering the same period with a payout for this payee
            var payRun = PayRun.Open(tenantId, Q2Start, Q2End, "engine", Guid.NewGuid(), Now);
            db.PayRuns.Add(payRun);
            await db.SaveChangesAsync();
            payRunId = payRun.Id;

            var payout = CompensationPayout.Calculate(
                tenantId, payeeId, planId,
                PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-001"),
                DateRange.Of(Q2Start, Q2End),
                [], Currency, "engine", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
            payout.AssignToRun(payRunId);
            db.CompensationPayouts.Add(payout);
            await db.SaveChangesAsync();
        }

        var jobSvc = new RecordingJobService();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId, jobSvc, new DirectDeletePayRunSender(db));
            var result = await handler.Handle(
                new RecalculateCreditsCommand(Q2Start, Q2End, PayeeId: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.BlockedByPayRuns.Should().BeNullOrEmpty("Draft run must not block");
            result.Value.DeletedDraftCount.Should().Be(1);
            result.Value.SupersededCount.Should().Be(1);
        }

        // Draft pay run must be gone
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var gone = await db.PayRuns.FindAsync(payRunId);
            gone.Should().BeNull("Draft pay run was auto-deleted by recalculate");

            var payouts = await db.CompensationPayouts.Where(p => p.PayRunId == payRunId).ToListAsync();
            payouts.Should().BeEmpty("payouts are cascade-deleted with the pay run");
        }
    }

    [Fact]
    public async Task Handle_BlocksOperation_WhenAffectedPayeesHaveApprovedPayRun()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid payRunId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlanFlat(tenantId, planId, rate: 0.04m));
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "TX-APPROVED-PR", payeeId, Money.Of(30_000m, Currency),
                new DateOnly(2026, 4, 15), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var alloc = CreateAllocator(db);
            var credits = await alloc.AllocateAsync(tx);
            foreach (var c in credits) db.Credits.Add(c);
            tx.MarkCalculated(1, credits[0].CreditedAmount, "engine", Now, Guid.NewGuid());
            await db.SaveChangesAsync();

            // Approved pay run — must block recalculation
            var payRun = PayRun.Open(tenantId, Q2Start, Q2End, "engine", Guid.NewGuid(), Now);
            payRun.Approve("engine", Now, Guid.NewGuid());
            db.PayRuns.Add(payRun);
            await db.SaveChangesAsync();
            payRunId = payRun.Id;

            var payout = CompensationPayout.Calculate(
                tenantId, payeeId, planId,
                PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-001"),
                DateRange.Of(Q2Start, Q2End),
                [], Currency, "engine", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
            payout.AssignToRun(payRunId);
            db.CompensationPayouts.Add(payout);
            await db.SaveChangesAsync();
        }

        var jobSvc = new RecordingJobService();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId, jobSvc);
            var result = await handler.Handle(
                new RecalculateCreditsCommand(Q2Start, Q2End, PayeeId: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue("result is Success; blocked state is in the value");
            result.Value!.BlockedByPayRuns.Should().HaveCount(1);
            result.Value.BlockedByPayRuns![0].PayRunId.Should().Be(payRunId);
            result.Value.BlockedByPayRuns[0].Status.Should().Be("Approved");
            result.Value.SupersededCount.Should().Be(0, "nothing must be touched when blocked");
            result.Value.DeletedDraftCount.Should().Be(0);
        }

        jobSvc.EnqueuedPayloads.Should().BeEmpty("no jobs enqueued when blocked");
    }

    [Fact]
    public async Task Handle_BlocksOperation_WhenAffectedPayeesHavePaidPayRun()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid payRunId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlanFlat(tenantId, planId, rate: 0.04m));
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "TX-PAID-PR", payeeId, Money.Of(20_000m, Currency),
                new DateOnly(2026, 6, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var alloc = CreateAllocator(db);
            var credits = await alloc.AllocateAsync(tx);
            foreach (var c in credits) db.Credits.Add(c);
            tx.MarkCalculated(1, credits[0].CreditedAmount, "engine", Now, Guid.NewGuid());
            await db.SaveChangesAsync();

            // Paid pay run — must block recalculation
            var payRun = PayRun.Open(tenantId, Q2Start, Q2End, "engine", Guid.NewGuid(), Now);
            payRun.Approve("engine", Now, Guid.NewGuid());
            payRun.MarkPaid("engine", Now, Guid.NewGuid());
            db.PayRuns.Add(payRun);
            await db.SaveChangesAsync();
            payRunId = payRun.Id;

            var payout = CompensationPayout.Calculate(
                tenantId, payeeId, planId,
                PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-001"),
                DateRange.Of(Q2Start, Q2End),
                [], Currency, "engine", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
            payout.AssignToRun(payRunId);
            db.CompensationPayouts.Add(payout);
            await db.SaveChangesAsync();
        }

        var jobSvc = new RecordingJobService();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId, jobSvc);
            var result = await handler.Handle(
                new RecalculateCreditsCommand(Q2Start, Q2End, PayeeId: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.BlockedByPayRuns.Should().HaveCount(1);
            result.Value.BlockedByPayRuns![0].Status.Should().Be("Paid");
            result.Value.SupersededCount.Should().Be(0, "nothing must be touched when blocked");
        }

        jobSvc.EnqueuedPayloads.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_BlocksAll_WhenMixOfDraftAndApprovedPayRuns()
    {
        // If any payee has an Approved/Paid run, block everything — no partial state.
        var tenantId = Guid.NewGuid();
        var payee1Id = Guid.NewGuid();
        var payee2Id = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid approvedRunId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.CompensationPlans.Add(MakePlanFlat(tenantId, planId, rate: 0.04m));
            db.Payees.Add(MakePayee(tenantId, payee1Id));
            db.Payees.Add(Payee.Create(tenantId, "Payee 2", "EMP-002", "p2@test.com",
                new DateOnly(2020, 1, 1), "test-user", payee2Id, Now));
            var payeeRef2 = PayeeReference.Snapshot(payee2Id, "Payee 2", "EMP-002");
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee1Id));
            db.PlanAssignments.Add(PlanAssignment.Create(tenantId, planId, payee2Id, payeeRef2,
                DateRange.Of(Q2Start, Q2End), "test-user", Guid.NewGuid(), Now, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx1 = CompensationTransaction.Ingest(
                tenantId, "TX-MIX-1", payee1Id, Money.Of(10_000m, Currency),
                new DateOnly(2026, 4, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            var tx2 = CompensationTransaction.Ingest(
                tenantId, "TX-MIX-2", payee2Id, Money.Of(10_000m, Currency),
                new DateOnly(2026, 4, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.AddRange(tx1, tx2);
            await db.SaveChangesAsync();

            var alloc = CreateAllocator(db);
            foreach (var tx in new[] { tx1, tx2 })
            {
                var c = await alloc.AllocateAsync(tx);
                foreach (var cr in c) db.Credits.Add(cr);
                tx.MarkCalculated(1, c[0].CreditedAmount, "engine", Now, Guid.NewGuid());
            }
            await db.SaveChangesAsync();

            // payee1 → Approved run (must block)
            var approvedRun = PayRun.Open(tenantId, Q2Start, Q2End, "engine", Guid.NewGuid(), Now);
            approvedRun.Approve("engine", Now, Guid.NewGuid());
            db.PayRuns.Add(approvedRun);
            await db.SaveChangesAsync();
            approvedRunId = approvedRun.Id;

            var payout1 = CompensationPayout.Calculate(
                tenantId, payee1Id, planId,
                PayeeReference.Snapshot(payee1Id, "Test Payee", "EMP-001"),
                DateRange.Of(Q2Start, Q2End),
                [], Currency, "engine", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
            payout1.AssignToRun(approvedRunId);
            db.CompensationPayouts.Add(payout1);

            // payee2 → Draft run (would be deleted, but should be blocked too)
            var draftRun = PayRun.Open(tenantId, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30),
                "engine", Guid.NewGuid(), Now);
            db.PayRuns.Add(draftRun);
            await db.SaveChangesAsync();

            var payout2 = CompensationPayout.Calculate(
                tenantId, payee2Id, planId,
                PayeeReference.Snapshot(payee2Id, "Payee 2", "EMP-002"),
                DateRange.Of(Q2Start, Q2End),
                [], Currency, "engine", Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
            payout2.AssignToRun(draftRun.Id);
            db.CompensationPayouts.Add(payout2);
            await db.SaveChangesAsync();
        }

        var jobSvc = new RecordingJobService();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId, jobSvc);
            var result = await handler.Handle(
                new RecalculateCreditsCommand(Q2Start, Q2End, PayeeId: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.BlockedByPayRuns.Should().NotBeNullOrEmpty("Approved run must block");
            result.Value.BlockedByPayRuns!.Should().Contain(r => r.PayRunId == approvedRunId);
            result.Value.SupersededCount.Should().Be(0, "nothing touched when blocked");
        }

        jobSvc.EnqueuedPayloads.Should().BeEmpty();
    }
}
