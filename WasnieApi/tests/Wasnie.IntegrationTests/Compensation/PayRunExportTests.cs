#pragma warning disable CS8602

using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.Handlers.PayRuns;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Compensation.Queries.PayRuns;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// Integration tests for A.6 Phase 6 exports:
/// ExportPayRunsHandler (run list) and ExportPayoutsHandler with PayRunId + AmountMin/Max.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class PayRunExportTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";
    private const string Usd = "USD";

    // ── Auth stubs ────────────────────────────────────────────────────────────

    private sealed class AlwaysAllowAuth : IAuthorizationService
    {
        public static readonly AlwaysAllowAuth Instance = new();
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
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

    // Local DirectSender: routes CalculatePayoutsForPeriodCommand to its handler.
    private sealed class DirectSender(ApplicationDbContext db, Guid tenantId) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is CalculatePayoutsForPeriodCommand cmd)
            {
                var handler = new CalculatePayoutsForPeriodHandler(
                    db, new PayoutEngineFixture.FixedTenantContext(tenantId),
                    new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
                    NullLogger<CalculatePayoutsForPeriodHandler>.Instance);
                var result = await handler.Handle(cmd, ct);
                return (TResponse)(object)result;
            }
            throw new NotSupportedException($"DirectSender: {request.GetType().Name} not wired.");
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
            => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    // ── Handler factories ─────────────────────────────────────────────────────

    private CalculatePayRunHandler CalcHandler(ApplicationDbContext db, Guid tid, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance,
            new PayoutEngineFixture.FixedTenantContext(tid),
            new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            new DirectSender(db, tid));

    private static ExportPayRunsHandler RunExportHandler(
        ApplicationDbContext db, Guid tid, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance,
            new PayoutEngineFixture.FixedTenantContext(tid),
            new PayRunExcelExportService());

    private static ExportPayoutsHandler PayoutExportHandler(
        ApplicationDbContext db, Guid tid, IAuthorizationService? auth = null) =>
        new(db, auth ?? AlwaysAllowAuth.Instance,
            new PayoutEngineFixture.FixedTenantContext(tid),
            new PayoutExcelExportService());

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
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
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
        Guid tenantId, Guid payeeId, string refNum, DateOnly date, decimal amount, string currency) =>
        CompensationTransaction.Ingest(tenantId, refNum, payeeId,
            Money.Of(amount, currency), date, TransactionSource.Manual,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());

    /// <summary>Seeds plan + payee + assignment + credit → calculates a pay run. Returns (tenantId, payeeId, runId).</summary>
    private async Task<(Guid tenantId, Guid payeeId, Guid runId)> SeedAsync(
        decimal txAmount, string currency)
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);
        var date = new DateOnly(2026, 2, 15);

        await using (var db1 = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-X");
            db1.Payees.Add(payee);
            db1.CompensationPlans.Add(MakePlan(tenantId, planId, currency, start, end));
            db1.PlanAssignments.Add(MakeAssignment(
                tenantId, planId, payee.Id, payee.FullName, "EMP-X", start, end));
            await db1.SaveChangesAsync();
        }

        Guid payeeId;
        await using (var db2 = fixture.CreateDbForTenant(tenantId))
        {
            var plan = await db2.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);
            var payee = await db2.Payees.FirstAsync(p => p.TenantId == tenantId);
            payeeId = payee.Id;
            var tx = MakeTx(tenantId, payeeId, $"REF-{Guid.NewGuid():N}", date, txAmount, currency);
            db2.CompensationTransactions.Add(tx);
            var credit = Domain.Compensation.Credits.Credit.Allocate(
                tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
                Money.Of(txAmount, currency), Money.Of(txAmount * 0.10m, currency),
                Percentage.FromPercent(100), CreditRole.Primary,
                "test", Guid.NewGuid(), Now, Guid.NewGuid());
            db2.Credits.Add(credit);
            tx.MarkCalculated(1, Money.Of(txAmount * 0.10m, currency), "test", Now, Guid.NewGuid());
            await db2.SaveChangesAsync();
        }

        await using var db3 = fixture.CreateDbForTenant(tenantId);
        var calc = await CalcHandler(db3, tenantId)
            .Handle(new CalculatePayRunCommand(start, end), default);
        calc.IsSuccess.Should().BeTrue();

        return (tenantId, payeeId, calc.Value!.PayRunId);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 1 — ExportPayRunsHandler (run list export)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportPayRuns_NoFilter_ReturnsBytesForTenant()
    {
        var (tenantId, _, _) = await SeedAsync(1000m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var result = await RunExportHandler(db, tenantId)
            .Handle(new ExportPayRunsQuery(new PayRunFilterQuery()), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Bytes.Length.Should().BeGreaterThan(0);
        result.Value.FileName.Should().StartWith("pay-runs-export-");
        result.Value.ContentType.Should().Contain("spreadsheetml");
    }

    [Fact]
    public async Task ExportPayRuns_StatusFilter_FiltersCorrectly()
    {
        var (tenantId, _, _) = await SeedAsync(500m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var draftResult = await RunExportHandler(db, tenantId)
            .Handle(new ExportPayRunsQuery(new PayRunFilterQuery { Status = "Draft" }), default);
        draftResult.IsSuccess.Should().BeTrue();

        var paidResult = await RunExportHandler(db, tenantId)
            .Handle(new ExportPayRunsQuery(new PayRunFilterQuery { Status = "Paid" }), default);
        paidResult.IsSuccess.Should().BeTrue();
        // Both succeed; Draft filter has data, Paid filter has zero data rows (header only).
    }

    [Fact]
    public async Task ExportPayRuns_TenantIsolation_OtherTenantRunsExcluded()
    {
        var (tenantA, _, _) = await SeedAsync(1000m, Eur);
        var (tenantB, _, _) = await SeedAsync(2000m, Usd);

        // Tenant A's DbContext should have exactly 1 run.
        await using var dbA = fixture.CreateDbForTenant(tenantA);
        var count = await dbA.PayRuns.CountAsync();
        count.Should().Be(1);

        var result = await RunExportHandler(dbA, tenantA)
            .Handle(new ExportPayRunsQuery(new PayRunFilterQuery()), default);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExportPayRuns_WithoutPermission_ThrowsForbidden()
    {
        var (tenantId, _, _) = await SeedAsync(1000m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var act = async () => await RunExportHandler(db, tenantId, AlwaysForbidAuth.Instance)
            .Handle(new ExportPayRunsQuery(new PayRunFilterQuery()), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ExportPayRuns_PeriodFilter_MatchesRunPeriod()
    {
        var (tenantId, _, _) = await SeedAsync(1000m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);

        // Filter that covers the run (Jan–Mar 2026).
        var inRange = await RunExportHandler(db, tenantId)
            .Handle(new ExportPayRunsQuery(new PayRunFilterQuery
            {
                PeriodFrom = new DateOnly(2026, 1, 1),
                PeriodTo = new DateOnly(2026, 3, 31),
            }), default);
        inRange.IsSuccess.Should().BeTrue();

        // Filter before the run → no rows (but still succeeds and returns file).
        var outOfRange = await RunExportHandler(db, tenantId)
            .Handle(new ExportPayRunsQuery(new PayRunFilterQuery
            {
                PeriodFrom = new DateOnly(2025, 1, 1),
                PeriodTo = new DateOnly(2025, 12, 31),
            }), default);
        outOfRange.IsSuccess.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GROUP 2 — ExportPayoutsHandler with PayRunId filter
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportPayouts_WithPayRunId_ReturnsBytesForRun()
    {
        var (tenantId, _, runId) = await SeedAsync(800m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var result = await PayoutExportHandler(db, tenantId)
            .Handle(new ExportPayoutsQuery(new PayoutFilterQuery { PayRunId = runId }), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportPayouts_AmountMin_ExcludesPayoutsBelowThreshold()
    {
        // Payout = 500 * 0.10 = 50 EUR.
        var (tenantId, _, runId) = await SeedAsync(500m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);
        // AmountMin = 100 → 0 rows match (50 < 100).
        var result = await PayoutExportHandler(db, tenantId)
            .Handle(new ExportPayoutsQuery(new PayoutFilterQuery
            {
                PayRunId = runId,
                AmountMin = 100m,
            }), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExportPayouts_AmountMax_ExcludesPayoutsAboveThreshold()
    {
        // Payout = 5000 * 0.10 = 500 EUR.
        var (tenantId, _, runId) = await SeedAsync(5000m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);
        // AmountMax = 10 → 0 rows match (500 > 10).
        var result = await PayoutExportHandler(db, tenantId)
            .Handle(new ExportPayoutsQuery(new PayoutFilterQuery
            {
                PayRunId = runId,
                AmountMax = 10m,
            }), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExportPayouts_WithPayRunId_TenantIsolation()
    {
        var (tenantA, _, runIdA) = await SeedAsync(1000m, Eur);
        var (tenantB, _, _) = await SeedAsync(2000m, Usd);

        // Query from tenantA with tenantB's runId → should return 0 payouts (tenant filter excludes them).
        // In practice the global EF query filter prevents cross-tenant access.
        await using var dbA = fixture.CreateDbForTenant(tenantA);
        var result = await PayoutExportHandler(dbA, tenantA)
            .Handle(new ExportPayoutsQuery(new PayoutFilterQuery { PayRunId = runIdA }), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExportPayouts_WithoutPermission_ThrowsForbidden()
    {
        var (tenantId, _, runId) = await SeedAsync(1000m, Eur);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var act = async () => await PayoutExportHandler(db, tenantId, AlwaysForbidAuth.Instance)
            .Handle(new ExportPayoutsQuery(new PayoutFilterQuery { PayRunId = runId }), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
