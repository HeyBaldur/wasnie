#pragma warning disable CS8602

using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.Handlers.PayRuns;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Compensation.Queries.PayRuns;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// Integration tests for the A.6 Pay Run Engine (CalculatePayRun, Approve, MarkPaid, Reopen).
/// Uses the same Testcontainers SQL Server fixture as PayoutEngineTests — real migrations, real SQL.
/// Each test uses a unique tenantId to avoid inter-test interference.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class PayRunEngineTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class AlwaysAllowAuth : IAuthorizationService
    {
        public static readonly AlwaysAllowAuth Instance = new();
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public static readonly NoOpAuditService Instance = new();
        public Task LogAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AlwaysForbidAuth : IAuthorizationService
    {
        public static readonly AlwaysForbidAuth Instance = new();
        public Task RequireAsync(string permission, CancellationToken ct = default)
            => throw new ForbiddenException(permission);
    }

    private sealed class FixedUser : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? Email => "test@test.com";
        public bool IsAuthenticated => true;
    }

    /// <summary>
    /// Routes CalculatePayoutsForPeriodCommand directly to its handler using the shared DbContext.
    /// This avoids spinning up a full DI container while exercising the real handler code.
    /// </summary>
    private sealed class DirectSender(ApplicationDbContext db, Guid tenantId) : ISender
    {
        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is CalculatePayoutsForPeriodCommand cmd)
            {
                var handler = new CalculatePayoutsForPeriodHandler(
                    db,
                    new PayoutEngineFixture.FixedTenantContext(tenantId),
                    new FixedUser(),
                    new FakeClock(Now.UtcDateTime),
                    new FakeGuidGenerator(),
                    NullLogger<CalculatePayoutsForPeriodHandler>.Instance);
                var result = await handler.Handle(cmd, ct);
                return (TResponse)(object)result;
            }
            throw new NotSupportedException($"DirectSender: command {request.GetType().Name} not wired.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    // ── Handler factories ─────────────────────────────────────────────────────

    private static CalculatePayRunHandler CalcRunHandler(
        ApplicationDbContext db, Guid tenantId,
        IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance,
            new PayoutEngineFixture.FixedTenantContext(tenantId),
            new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            new DirectSender(db, tenantId));

    private static ApprovePayRunHandler ApproveHandler(
        ApplicationDbContext db, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance,
            new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            NoOpAuditService.Instance);

    private static MarkPayRunPaidHandler MarkPaidHandler(
        ApplicationDbContext db, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance,
            new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            NoOpAuditService.Instance,
            NullLogger<MarkPayRunPaidHandler>.Instance);

    private static ReopenPayRunHandler ReopenHandler(
        ApplicationDbContext db, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance,
            new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator());

    private static ListPayRunsHandler ListHandler(
        ApplicationDbContext db, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance);

    private static GetPayRunByIdHandler GetByIdHandler(
        ApplicationDbContext db, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance);

    // ── Data helpers ──────────────────────────────────────────────────────────

    private static Payee MakePayee(Guid tenantId, string code) =>
        Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);

    private static Plan MakePlan(Guid tenantId, Guid planId, string currency,
        DateOnly start, DateOnly end, decimal rate = 0.10m)
    {
        var plan = Plan.Create(tenantId, $"Plan {currency}", "desc",
            DateRange.Of(start, end), currency, "test", planId, Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum
            },
            RateTable.Flat(rate));
        return plan;
    }

    private static PlanAssignment MakeAssignment(
        Guid tenantId, Guid planId, Guid payeeId, string name, string code,
        DateOnly start, DateOnly end) =>
        PlanAssignment.Create(tenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, name, code),
            DateRange.Of(start, end), "test", Guid.NewGuid(), Now, Guid.NewGuid());

    private static CompensationTransaction MakeTx(
        Guid tenantId, Guid payeeId, string refNum, DateOnly date,
        decimal amount, string currency) =>
        CompensationTransaction.Ingest(tenantId, refNum, payeeId,
            Money.Of(amount, currency), date,
            TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid());

    /// <summary>Seeds plan + payee + assignment. No credits → payout will be $0.</summary>
    private async Task<(Guid tenantId, Guid planId, Guid payeeId)> SeedEmptyRunAsync(
        string currency = Eur, DateOnly? start = null, DateOnly? end = null)
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var periodStart = start ?? new DateOnly(2026, 1, 1);
        var periodEnd = end ?? new DateOnly(2026, 3, 31);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var payee = MakePayee(tenantId, "EMP-TEST");
        db.Payees.Add(payee);
        db.CompensationPlans.Add(MakePlan(tenantId, planId, currency, periodStart, periodEnd));
        db.PlanAssignments.Add(MakeAssignment(
            tenantId, planId, payee.Id, payee.FullName, "EMP-TEST", periodStart, periodEnd));
        await db.SaveChangesAsync();

        return (tenantId, planId, payee.Id);
    }

    /// <summary>Seeds plan + payee + assignment + one credited transaction → payout > $0.</summary>
    private async Task<(Guid tenantId, Guid planId, Guid payeeId)> SeedRunWithCreditAsync(
        decimal amount, string currency, DateOnly? txDate = null)
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);
        var date = txDate ?? new DateOnly(2026, 2, 15);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var payee = MakePayee(tenantId, "EMP-CASH");
        db.Payees.Add(payee);
        db.CompensationPlans.Add(MakePlan(tenantId, planId, currency, start, end));
        db.PlanAssignments.Add(MakeAssignment(
            tenantId, planId, payee.Id, payee.FullName, "EMP-CASH", start, end));
        await db.SaveChangesAsync();

        await using var db2 = fixture.CreateDbForTenant(tenantId);
        var plan = await db2.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
        var ruleId = plan.Rules.First().Id;
        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
            RateTable.Flat(0.10m), Trigger.Always(), Now);
        var tx = MakeTx(tenantId, payee.Id, $"REF-{Guid.NewGuid():N}", date, amount, currency);
        db2.CompensationTransactions.Add(tx);
        var credit = Domain.Compensation.Credits.Credit.Allocate(
            tenantId, tx.Id, payee.Id, planId, ruleId, snapshot,
            Money.Of(amount, currency), Money.Of(amount * 0.10m, currency),
            Percentage.FromPercent(100), CreditRole.Primary,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        db2.Credits.Add(credit);
        tx.MarkCalculated(1, Money.Of(amount * 0.10m, currency), "test", Now, Guid.NewGuid());
        await db2.SaveChangesAsync();

        return (tenantId, planId, payee.Id);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 1: CalculatePayRun — idempotency
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CalculatePayRun_FirstCall_CreatesDraftRunAndAssignsPayRunId()
    {
        var (tenantId, planId, payeeId) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        CalculatePayRunResult result;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            result = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!;
        }

        result.PayRunId.Should().NotBeEmpty();
        result.PayoutsCreated.Should().Be(1);
        result.Conflicts.Should().BeEmpty();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstOrDefaultAsync(r => r.Id == result.PayRunId);
            run.Should().NotBeNull();
            run!.Status.Should().Be(PayRunStatus.Draft);
            run.PayeeCount.Should().Be(1);

            var payout = await db.CompensationPayouts
                .FirstAsync(p => p.PayeeId == payeeId && p.PlanId == planId);
            payout.PayRunId.Should().Be(result.PayRunId,
                "CalculatePayRun must assign PayRunId to every payout it creates");
        }
    }

    [Fact]
    public async Task CalculatePayRun_DraftRunExists_ReusesDraftRunNoDuplicate()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid firstRunId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            firstRunId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        Guid secondRunId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            secondRunId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        secondRunId.Should().Be(firstRunId, "second calculate must reuse the Draft run, not create a new one");

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var count = await db.PayRuns.CountAsync(r => r.PeriodStart == start && r.PeriodEnd == end);
            count.Should().Be(1, "no duplicate PayRun rows allowed for the same period");
        }
    }

    [Fact]
    public async Task CalculatePayRun_ApprovedRun_CreatesSupplementalRunSequence1()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        // Calculate then approve the primary run.
        Guid primaryRunId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            primaryRunId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(primaryRunId), CancellationToken.None);
        }

        // Second calculate on Approved period → must succeed as supplemental #1.
        CalculatePayRunResult supplementalResult;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None);
            result.IsSuccess.Should().BeTrue("an Approved period must allow a supplemental run");
            supplementalResult = result.Value!;
        }

        supplementalResult.IsSupplemental.Should().BeTrue();
        supplementalResult.SupplementalSequence.Should().Be(1);
        supplementalResult.PayRunId.Should().NotBe(primaryRunId, "supplemental must be a new run");

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var runCount = await db.PayRuns.CountAsync(r => r.PeriodStart == start && r.PeriodEnd == end);
            runCount.Should().Be(2, "primary (Approved) + supplemental (Draft) must coexist");

            var supplemental = await db.PayRuns.FirstAsync(r => r.Id == supplementalResult.PayRunId);
            supplemental.Status.Should().Be(PayRunStatus.Draft);
            supplemental.SupplementalSequence.Should().Be(1);
        }
    }

    [Fact]
    public async Task CalculatePayRun_PaidRun_CreatesSupplementalRunSequence1()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid primaryRunId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            primaryRunId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(primaryRunId), CancellationToken.None);
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(primaryRunId), CancellationToken.None);
        }

        // Calculate on Paid period → supplemental run created instead of blocking.
        CalculatePayRunResult supplementalResult;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None);
            result.IsSuccess.Should().BeTrue("a Paid period must allow a supplemental run for new payees");
            supplementalResult = result.Value!;
        }

        supplementalResult.IsSupplemental.Should().BeTrue();
        supplementalResult.SupplementalSequence.Should().Be(1);
        supplementalResult.PayRunId.Should().NotBe(primaryRunId);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var supplemental = await db.PayRuns.FirstAsync(r => r.Id == supplementalResult.PayRunId);
            supplemental.Status.Should().Be(PayRunStatus.Draft);
            supplemental.SupplementalSequence.Should().Be(1);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 2: State machine transitions (valid + invalid)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApprovePayRun_DraftRun_CascadesPayoutsToApproved()
    {
        var (tenantId, planId, payeeId) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await ApproveHandler(db).Handle(new ApprovePayRunCommand(runId), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstAsync(r => r.Id == runId);
            run.Status.Should().Be(PayRunStatus.Approved);
            run.ApprovedAt.Should().NotBeNull();
            run.ApprovedBy.Should().Be("test@test.com");

            var payout = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeId);
            payout.Status.Should().Be(CompensationPayoutStatus.Approved,
                "cascade must transition all Calculated payouts to Approved");
        }
    }

    [Fact]
    public async Task MarkPayRunPaid_ApprovedRun_CascadesPayoutsToPaid()
    {
        var (tenantId, planId, payeeId) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(runId), CancellationToken.None);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runId), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstAsync(r => r.Id == runId);
            run.Status.Should().Be(PayRunStatus.Paid);
            run.PaidAt.Should().NotBeNull();

            var payout = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeId);
            payout.Status.Should().Be(CompensationPayoutStatus.Paid,
                "cascade must transition all Approved payouts to Paid");
        }
    }

    [Fact]
    public async Task ReopenPayRun_ApprovedRun_RevertsToDraftAndRevertsCascade()
    {
        var (tenantId, planId, payeeId) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(runId), CancellationToken.None);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await ReopenHandler(db).Handle(new ReopenPayRunCommand(runId), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstAsync(r => r.Id == runId);
            run.Status.Should().Be(PayRunStatus.Draft,
                "Reopen must revert run from Approved to Draft");
            run.ApprovedAt.Should().BeNull("ApprovedAt must be cleared on Reopen");
            run.ApprovedBy.Should().BeNull();

            var payout = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeId);
            payout.Status.Should().Be(CompensationPayoutStatus.Calculated,
                "Reopen must revert Approved payouts to Calculated via RevertToCalculated()");
        }
    }

    [Fact]
    public async Task ReopenPayRun_PaidRun_ReturnsFailureClean()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(runId), CancellationToken.None);
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runId), CancellationToken.None);
        }

        // Reopen on Paid must return clean failure, NOT a 500 / exception.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await ReopenHandler(db).Handle(new ReopenPayRunCommand(runId), CancellationToken.None);
            result.IsSuccess.Should().BeFalse("Paid run is locked — Reopen must be rejected");
            result.Error.Should().Contain("Paid");
        }

        // Run must remain Paid — no partial state change.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstAsync(r => r.Id == runId);
            run.Status.Should().Be(PayRunStatus.Paid, "Paid run must not be modified by a failed Reopen");
        }
    }

    [Fact]
    public async Task MarkPayRunPaid_DraftRun_ReturnsFailure()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runId), CancellationToken.None);
            result.IsSuccess.Should().BeFalse("cannot mark Draft run as Paid — must Approve first");
        }
    }

    [Fact]
    public async Task ApprovePayRun_PaidRun_ReturnsFailure()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(runId), CancellationToken.None);
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runId), CancellationToken.None);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await ApproveHandler(db).Handle(new ApprovePayRunCommand(runId), CancellationToken.None);
            result.IsSuccess.Should().BeFalse("Paid run cannot be approved again");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 3: Roll-ups
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CalculatePayRun_SingleCurrencyWithCredit_TotalAmountsPopulatedCorrectly()
    {
        // 1000 EUR × 10% = 100 EUR commission
        var (tenantId, _, _) = await SeedRunWithCreditAsync(1000m, Eur);
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstAsync(r => r.Id == runId);
            run.PayeeCount.Should().Be(1);
            run.PaidPayeeCount.Should().Be(1, "payout has commission > 0");
            run.ZeroPayoutCount.Should().Be(0);
            run.TotalAmounts.Should().ContainKey(Eur);
            run.TotalAmounts[Eur].Should().Be(100m, "1000 × 10% = 100 EUR");
        }
    }

    [Fact]
    public async Task CalculatePayRun_ZeroPayout_PayeeCountIncludesItPaidPayeeCountExcludes()
    {
        // Seed plan+assignment but no credits → $0 payout.
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstAsync(r => r.Id == runId);
            run.PayeeCount.Should().Be(1, "PayeeCount includes $0 payees");
            run.PaidPayeeCount.Should().Be(0, "PaidPayeeCount excludes $0 payees");
            run.ZeroPayoutCount.Should().Be(1);
            run.TotalAmounts.Should().BeEmpty("no positive amounts — TotalAmounts excludes $0 payees");
        }
    }

    [Fact]
    public async Task UpdateRollUps_AntiCartesian_TotalAmountsAreNotInflated()
    {
        // Explicit Cartesian-guard test on roll-ups.
        // Tenant has ONE payee with 5 credits of 100 EUR each = 500 EUR total commission.
        // UpdateRollUps must sum once per credit, not cross-join.
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-ANTI");
            db.Payees.Add(payee);
            db.CompensationPlans.Add(MakePlan(tenantId, planId, Eur, start, end, 0.10m));
            db.PlanAssignments.Add(MakeAssignment(
                tenantId, planId, payee.Id, payee.FullName, "EMP-ANTI", start, end));
            await db.SaveChangesAsync();

            var plan = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            for (var i = 1; i <= 5; i++)
            {
                var tx = MakeTx(tenantId, payee.Id, $"REF-ANTI-{i:D2}",
                    new DateOnly(2026, 2, i), 1000m, Eur);
                db.CompensationTransactions.Add(tx);
                var credit = Domain.Compensation.Credits.Credit.Allocate(
                    tenantId, tx.Id, payee.Id, planId, ruleId, snapshot,
                    Money.Of(1000m, Eur), Money.Of(100m, Eur),
                    Percentage.FromPercent(100), CreditRole.Primary,
                    "test", Guid.NewGuid(), Now, Guid.NewGuid());
                db.Credits.Add(credit);
                tx.MarkCalculated(1, Money.Of(100m, Eur), "test", Now, Guid.NewGuid());
            }
            await db.SaveChangesAsync();
        }

        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var run = await db.PayRuns.FirstAsync(r => r.Id == runId);
            run.TotalAmounts[Eur].Should().Be(500m,
                "5 credits × 100 EUR = 500 EUR — Cartesian inflation would give 500 × N");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 4: Multi-tenant isolation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PayRuns_TenantIsolation_TenantACannotSeeTenantBRun()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        // Create a run in tenant B.
        var (tenantIdB, _, _) = await SeedEmptyRunAsync();
        Guid runIdB;
        await using (var db = fixture.CreateDbForTenant(tenantIdB))
        {
            runIdB = (await CalcRunHandler(db, tenantIdB)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        // Tenant A's context must not see tenant B's run.
        var tenantIdA = Guid.NewGuid();
        await using (var db = fixture.CreateDbForTenant(tenantIdA))
        {
            // HasQueryFilter scopes to tenantIdA — run from B must be invisible.
            var run = await db.PayRuns.FirstOrDefaultAsync(r => r.Id == runIdB);
            run.Should().BeNull("HasQueryFilter must prevent cross-tenant PayRun access");

            // GetPayRunByIdHandler must also not find it.
            var result = await GetByIdHandler(db).Handle(
                new GetPayRunByIdQuery(runIdB, new PayRunPayoutsFilter()),
                CancellationToken.None);
            result.IsSuccess.Should().BeFalse("handler must return not-found, not the other tenant's run");
        }
    }

    [Fact]
    public async Task PayRuns_TwoTenants_SamePeriodBothAllowed()
    {
        // Unique index is (TenantId, PeriodStart, PeriodEnd) — two tenants with same period is fine.
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        var (tenantA, _, _) = await SeedEmptyRunAsync();
        var (tenantB, _, _) = await SeedEmptyRunAsync();

        Guid runA, runB;
        await using (var db = fixture.CreateDbForTenant(tenantA))
        {
            runA = (await CalcRunHandler(db, tenantA)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantB))
        {
            // Must succeed — unique index is per-tenant, not global.
            var result = await CalcRunHandler(db, tenantB)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None);
            result.IsSuccess.Should().BeTrue("same period is allowed for a different tenant");
            runB = result.Value!.PayRunId;
        }

        runA.Should().NotBe(runB, "each tenant gets its own PayRun");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 5: Permission gates
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CalculatePayRun_WithoutCalculatePermission_ThrowsForbidden()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        await using var db = fixture.CreateDbForTenant(tenantId);

        var act = () => CalcRunHandler(db, tenantId, AlwaysForbidAuth.Instance)
            .Handle(new CalculatePayRunCommand(
                new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage($"*{Permission.PayoutsCalculate}*");
    }

    [Fact]
    public async Task ApprovePayRun_WithoutApprovePermission_ThrowsForbidden()
    {
        await using var db = fixture.CreateDbForTenant(Guid.NewGuid());

        var act = () => ApproveHandler(db, AlwaysForbidAuth.Instance)
            .Handle(new ApprovePayRunCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage($"*{Permission.PayoutsApprove}*");
    }

    [Fact]
    public async Task MarkPayRunPaid_WithoutMarkPaidPermission_ThrowsForbidden()
    {
        await using var db = fixture.CreateDbForTenant(Guid.NewGuid());

        var act = () => MarkPaidHandler(db, AlwaysForbidAuth.Instance)
            .Handle(new MarkPayRunPaidCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage($"*{Permission.PayoutsMarkPaid}*");
    }

    [Fact]
    public async Task ReopenPayRun_WithoutReopenPermission_ThrowsForbidden()
    {
        await using var db = fixture.CreateDbForTenant(Guid.NewGuid());

        var act = () => ReopenHandler(db, AlwaysForbidAuth.Instance)
            .Handle(new ReopenPayRunCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage($"*{Permission.PayoutsReopen}*");
    }

    [Fact]
    public async Task ListPayRuns_WithoutReadPermission_ThrowsForbidden()
    {
        await using var db = fixture.CreateDbForTenant(Guid.NewGuid());

        var act = () => ListHandler(db, AlwaysForbidAuth.Instance)
            .Handle(new ListPayRunsQuery(new PayRunFilterQuery()), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage($"*{Permission.PayoutsRead}*");
    }

    // ── Anti-double-pay guard via pay-run path ────────────────────────────────
    // Reproduces the SMOCK-326546143213 scenario via the UI path (mark pay run paid).
    // Two pay runs calculated before any payment → same credit in both → paying run 1
    // consumes the credit → trying to pay run 2 must BLOCK with a clear error.

    [Fact]
    public async Task MarkPayRunPaid_WhenCreditAlreadyConsumedByAnotherPayout_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var periodA = (Start: new DateOnly(2026, 1, 1), End: new DateOnly(2026, 1, 31));
        var periodB = (Start: new DateOnly(2026, 1, 1), End: new DateOnly(2026, 3, 31));

        // Seed: payee + plan covering Jan-Mar + assignment + one tx on Jan 15.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-PAYRUN-DBL");
            db.Payees.Add(payee);
            db.CompensationPlans.Add(MakePlan(tenantId, planId, Eur, periodB.Start, periodB.End));
            db.PlanAssignments.Add(MakeAssignment(
                tenantId, planId, payee.Id, payee.FullName, "EMP-PAYRUN-DBL",
                periodB.Start, periodB.End));
            await db.SaveChangesAsync();

            await using var db2 = fixture.CreateDbForTenant(tenantId);
            var plan = await db2.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);
            var tx = MakeTx(tenantId, payee.Id, "SMOCK-PAYRUN-DBL-001",
                new DateOnly(2026, 1, 15), 5000m, Eur);
            db2.CompensationTransactions.Add(tx);
            var credit = Domain.Compensation.Credits.Credit.Allocate(
                tenantId, tx.Id, payee.Id, planId, ruleId, snapshot,
                Money.Of(5000m, Eur), Money.Of(500m, Eur),
                Percentage.FromPercent(100), CreditRole.Primary,
                "test", Guid.NewGuid(), Now, Guid.NewGuid());
            db2.Credits.Add(credit);
            tx.MarkCalculated(1, Money.Of(500m, Eur), "test", Now, Guid.NewGuid());
            await db2.SaveChangesAsync();
        }

        // Calculate pay run A (Jan 1-31) — captures the credit for Jan 15 tx.
        Guid runAId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(periodA.Start, periodA.End), default);
            r.IsSuccess.Should().BeTrue();
            runAId = r.Value!.PayRunId;
        }

        // Calculate pay run B (Jan 1-Mar 31) before paying A — same credit captured again.
        Guid runBId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(periodB.Start, periodB.End), default);
            r.IsSuccess.Should().BeTrue();
            runBId = r.Value!.PayRunId;
        }

        // Approve both runs.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(runAId), default);
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(runBId), default);
        }

        // Pay run A — must succeed and consume the credit.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runAId), default);
            r.IsSuccess.Should().BeTrue("pay run A should pay successfully");
        }

        // Try to pay run B — must BLOCK because the credit is consumed by run A's payout.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runBId), default);
            r.IsSuccess.Should().BeTrue("block is a structured domain outcome, not a handler failure");
            r.Value.Should().NotBeNull("blocked result carries structured conflict data");
            r.Value!.TotalConflicts.Should().BeGreaterThan(0, "at least one conflict must be reported");
        }

        // Verify pay run B is still Approved (not Paid).
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var runB = await db.PayRuns.FirstAsync(r => r.Id == runBId);
            runB.Status.Should().Be(PayRunStatus.Approved,
                "pay run B must remain Approved — no double payment occurred");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 6: Supplemental run sequences
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CalculatePayRun_TwoSupplementals_SequenceIncrementsCorrectly()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        // Primary run → approve → paid.
        Guid primaryRunId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            primaryRunId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(primaryRunId), CancellationToken.None);
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(primaryRunId), CancellationToken.None);
        }

        // Supplemental #1.
        Guid supp1Id;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!;
            r.SupplementalSequence.Should().Be(1);
            supp1Id = r.PayRunId;
        }

        // Approve + pay supplemental #1 so a second supplemental can be created.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(supp1Id), CancellationToken.None);
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(supp1Id), CancellationToken.None);
        }

        // Supplemental #2.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!;
            r.SupplementalSequence.Should().Be(2);
            r.IsSupplemental.Should().BeTrue();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var count = await db.PayRuns.CountAsync(r => r.PeriodStart == start && r.PeriodEnd == end);
            count.Should().Be(3, "primary (seq=0) + supp1 (seq=1) + supp2 (seq=2)");
        }
    }

    [Fact]
    public async Task CalculatePayRun_SupplementalDraft_ReusesDraftNotCreatesAnother()
    {
        var (tenantId, _, _) = await SeedEmptyRunAsync();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        // Primary run → approved.
        Guid primaryRunId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            primaryRunId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await ApproveHandler(db).Handle(new ApprovePayRunCommand(primaryRunId), CancellationToken.None);
        }

        // Create supplemental Draft.
        Guid supp1Id;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            supp1Id = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!.PayRunId;
        }

        // Calculate again — must reuse the supplemental Draft, not create a third run.
        Guid secondCallId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(start, end), CancellationToken.None)).Value!;
            r.IsSupplemental.Should().BeFalse("reusing an existing Draft is not 'creating' a supplemental");
            secondCallId = r.PayRunId;
        }

        secondCallId.Should().Be(supp1Id, "recalculating must reuse the existing supplemental Draft");

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var count = await db.PayRuns.CountAsync(r => r.PeriodStart == start && r.PeriodEnd == end);
            count.Should().Be(2, "primary (Approved, seq=0) + supp1 (Draft, seq=1) — no third run");
        }
    }
}
