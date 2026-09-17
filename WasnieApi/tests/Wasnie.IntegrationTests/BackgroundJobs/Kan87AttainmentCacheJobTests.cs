using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Models.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.BackgroundJobs;

/// <summary>
/// KAN-87, end to end: the REAL background job, over REAL SQL Server, on data built for this test.
///
/// ★★ WHY THIS CANNOT BE A UNIT TEST, AND THE ATTEMPT THAT PROVED IT. The job wraps its work in an
/// explicit database transaction (<c>ProcessPendingTransactionsJobHandler.cs:180</c>), and the EF
/// in-memory provider refuses to open one. So the job path — the one that commits per transaction and
/// therefore the only one where this defect can appear — genuinely needs a relational store.
/// <c>Kan87AttainmentCacheTests</c> covers the service in isolation; this covers the whole machine.
///
/// ★★ EVERY PLAN AND PAYEE HERE IS CREATED BY THE TEST. Nothing is read from an existing dataset: the
/// quota, the prior revenue, the tiers and the two sales are all built to make the defect visible if
/// it ever returns, rather than hoping some real row happens to have the right shape.
///
/// ★ THE STUB THE OTHER JOB TESTS USE IS DELIBERATELY NOT USED. <c>ProcessPendingJobTests</c> hands the
/// allocator a <c>StubQuotaAttainmentService</c>, which is right for what those tests are about and
/// useless here: the real service IS the subject.
/// </summary>
public sealed class Kan87AttainmentCacheJobTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private string _connectionString = string.Empty;

    private const string EUR = "EUR";
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Both sales of the run fall on this day — the collision the defect needed.</summary>
    private static readonly DateOnly SaleDate = new(2026, 3, 15);
    private static readonly DateOnly PeriodStart = new(2026, 1, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 12, 31);

    /// <summary>Quota 100,000 with 90,000 already credited, so one 30,000 sale crosses the boundary.</summary>
    private const decimal QuotaAmount = 100000m;
    private const decimal PriorRevenue = 90000m;
    private const decimal Sale = 30000m;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        await using var db = CreateDb(Guid.Empty);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // ── Harness ───────────────────────────────────────────────────────────────

    private ApplicationDbContext CreateDb(Guid tenantId) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenantContext(tenantId), NoOpPublisher.Instance);

    private static ProcessPendingTransactionsJobHandler Handler(ApplicationDbContext db)
    {
        var clock = new FakeClock(Now.UtcDateTime);
        var guid = new FakeGuidGenerator();

        var allocation = new CreditAllocationService(
            db, guid, clock, NullLogger<CreditAllocationService>.Instance,
            new QuotaAttainmentService(db));

        return new ProcessPendingTransactionsJobHandler(
            db, clock, guid, allocation, NullLogger<ProcessPendingTransactionsJobHandler>.Instance);
    }

    /// <summary>8% up to quota, 12% from quota to 150%, 15% above.</summary>
    private static AttainmentTier[] Tiers() =>
    [
        new() { AttainmentFrom = 0m,   AttainmentTo = 1m,   Rate = 0.08m },
        new() { AttainmentFrom = 1m,   AttainmentTo = 1.5m, Rate = 0.12m },
        new() { AttainmentFrom = 1.5m, AttainmentTo = null, Rate = 0.15m },
    ];

    private sealed record Lab(Guid TenantId, Guid PayeeId, Guid PlanId, Guid AssignmentId);

    private async Task<Lab> SeedAsync(string planName, bool splitAtQuota, bool withCascade)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);

        var payee = Payee.Create(tenantId, "KAN87 Payee", "KAN87-P1", "kan87@test.local",
            new DateOnly(2020, 1, 1), "kan87", Guid.NewGuid(), Now);

        var plan = Plan.Create(tenantId, planName, "KAN-87 lab", DateRange.Of(PeriodStart, PeriodEnd),
            EUR, "kan87", Guid.NewGuid(), Now, Guid.NewGuid());

        plan.AddRule(planName + " rule", 1,
            new Measurement { Type = MeasurementType.Revenue },
            RateTable.AttainmentBased(Tiers(), splitAtQuota: splitAtQuota),
            modifier: withCascade ? new Modifier { Type = ModifierType.Accelerator, Factor = 1.2m } : null,
            cap: withCascade ? new Cap { Amount = Money.Of(8000m, EUR), Scope = CapScope.PerTransaction } : null,
            floor: withCascade ? new Floor { Amount = Money.Of(2000m, EUR) } : null);
        plan.Activate("kan87", Now, Guid.NewGuid());

        var assignment = PlanAssignment.Create(tenantId, plan.Id, payee.Id,
            PayeeReference.Snapshot(payee.Id, "KAN87 Payee", "KAN87-P1"),
            DateRange.Of(PeriodStart, PeriodEnd), "kan87", Guid.NewGuid(), Now, Guid.NewGuid());

        var quota = Quota.Create(tenantId, payee.Id, plan.Id, Money.Of(QuotaAmount, EUR),
            DateRange.Of(PeriodStart, PeriodEnd), QuotaMeasurementType.Revenue,
            "kan87", Guid.NewGuid(), Now);
        quota.Activate("kan87", Now, Guid.NewGuid());

        db.Payees.Add(payee);
        db.CompensationPlans.Add(plan);
        db.PlanAssignments.Add(assignment);
        db.Quotas.Add(quota);
        await db.SaveChangesAsync();

        // The prior attainment, as the engine measures it: a sale that already carries a live credit.
        var priorTx = Tx(tenantId, payee.Id, "KAN87-PRIOR", PriorRevenue, new DateOnly(2026, 2, 1));
        var snapshot = RuleSnapshot.Freeze(plan.Rules[0].Id, plan.Id, plan.Version, "prior",
            RateTable.Flat(0.08m), Trigger.Always(), Now,
            measurement: new Measurement { Type = MeasurementType.Revenue });
        db.CompensationTransactions.Add(priorTx);
        db.Credits.Add(Credit.Allocate(tenantId, priorTx.Id, payee.Id, plan.Id, plan.Rules[0].Id, snapshot,
            Money.Of(PriorRevenue, EUR), Money.Of(PriorRevenue * 0.08m, EUR),
            Percentage.FromPercent(100m), CreditRole.Primary, "kan87", Guid.NewGuid(), Now, Guid.NewGuid()));
        await db.SaveChangesAsync();

        return new Lab(tenantId, payee.Id, plan.Id, assignment.Id);
    }

    private static CompensationTransaction Tx(
        Guid tenantId, Guid payeeId, string reference, decimal amount, DateOnly date) =>
        CompensationTransaction.Ingest(tenantId, reference, payeeId, Money.Of(amount, EUR), date,
            TransactionSource.EtlImport, "kan87", Guid.NewGuid(), Now, Guid.NewGuid());

    private async Task AddPendingAsync(Lab lab, params string[] references)
    {
        await using var db = CreateDb(lab.TenantId);
        foreach (var reference in references)
            db.CompensationTransactions.Add(Tx(lab.TenantId, lab.PayeeId, reference, Sale, SaleDate));
        await db.SaveChangesAsync();
    }

    private async Task RunJobAsync(Lab lab)
    {
        await using var db = CreateDb(lab.TenantId);
        var payload = new ProcessPendingTransactionsPayload(
            lab.TenantId, ProcessPendingScope.ByPlanAssignment, lab.AssignmentId,
            null, null, "kan87", "kan87@test.local");
        await Handler(db).HandleAsync(payload, new JobContext(Guid.NewGuid(), new NoOpJobService()),
            CancellationToken.None);
    }

    /// <summary>The credited amounts of the run's sales, read back from the ledger.</summary>
    private async Task<List<decimal>> LedgerAmountsAsync(Lab lab)
    {
        await using var db = CreateDb(lab.TenantId);
        var priorTxId = await db.CompensationTransactions
            .Where(t => t.ReferenceNumber == "KAN87-PRIOR").Select(t => t.Id).SingleAsync();

        return await db.Credits
            .Where(c => c.PlanId == lab.PlanId && c.SupersededAt == null && c.TransactionId != priorTxId)
            .Select(c => c.CreditedAmount.Amount)
            .ToListAsync();
    }

    // ── The defect, end to end ────────────────────────────────────────────────

    /// <summary>
    /// ★★ TWO SALES OF THE SAME DAY IN ONE RUN MUST NOT BOTH BE PRICED BELOW QUOTA. Prior revenue is
    /// 90,000 of a 100,000 quota, so the first 30,000 sale is priced at 8% (2,400) and takes the payee
    /// to 120%; the second must then be priced at 12% (3,600).
    ///
    /// Before the fix both came out at 2,400, because the attainment reading was memoised under the
    /// shared (payee, plan, DATE) key and the second sale never saw its sibling's credit.
    ///
    /// ★ THE ASSERTION IS ON THE SET, NOT ON WHICH REFERENCE GOT WHICH AMOUNT. The job does not promise
    /// an order within a chunk, and pinning one would be testing an accident.
    /// </summary>
    [Fact]
    public async Task TwoSalesOfTheSameDayInOneRun_TheSecondIsPricedAboveQuota()
    {
        var lab = await SeedAsync("Plan A - Attainment Escalonado", splitAtQuota: false, withCascade: false);
        await AddPendingAsync(lab, "A-VENTA-1", "A-VENTA-2");

        await RunJobAsync(lab);

        (await LedgerAmountsAsync(lab)).Should().BeEquivalentTo(new[] { 2400m, 3600m });
    }

    /// <summary>
    /// ★★ DETERMINISM: THE PROPERTY THAT WAS ACTUALLY BROKEN. The same two sales, processed in two
    /// separate runs instead of one, must produce the same amounts. Before the fix the one-run case
    /// paid 2,400 twice and the two-run case paid 2,400 and 3,600 — the same work, worth 1,200 more
    /// depending only on how it happened to be batched.
    /// </summary>
    [Fact]
    public async Task TheSameTwoSalesPayTheSameWhetherProcessedTogetherOrSeparately()
    {
        var together = await SeedAsync("Plan A - juntas", splitAtQuota: false, withCascade: false);
        await AddPendingAsync(together, "A-VENTA-1", "A-VENTA-2");
        await RunJobAsync(together);

        var apart = await SeedAsync("Plan A - separadas", splitAtQuota: false, withCascade: false);
        await AddPendingAsync(apart, "A-VENTA-1");
        await RunJobAsync(apart);
        await AddPendingAsync(apart, "A-VENTA-2");
        await RunJobAsync(apart);

        var togetherAmounts = (await LedgerAmountsAsync(together)).OrderBy(a => a);
        var apartAmounts = (await LedgerAmountsAsync(apart)).OrderBy(a => a);

        togetherAmounts.Should().Equal(apartAmounts);
        togetherAmounts.Sum().Should().Be(6000m);
    }

    // ── The controls: what must NOT have changed ──────────────────────────────

    /// <summary>
    /// ★ SPLIT ON WAS ALREADY CORRECT AND STAYS CORRECT. It never cached, so the fix must not move it.
    /// First sale: 10,000 of the 30,000 still fits under the 100,000 quota at 8% (800) and the
    /// remaining 20,000 is above it at 12% (2,400) = 3,200. Second sale: entirely inside the 100–150%
    /// band at 12% = 3,600.
    /// </summary>
    [Fact]
    public async Task SplitAtQuotaIsUnchanged()
    {
        var lab = await SeedAsync("Plan B - Split ON", splitAtQuota: true, withCascade: false);
        await AddPendingAsync(lab, "B-VENTA-1", "B-VENTA-2");

        await RunJobAsync(lab);

        (await LedgerAmountsAsync(lab)).Should().BeEquivalentTo(new[] { 3200m, 3600m });
    }

    /// <summary>
    /// ★ THE CASCADE IS UNCHANGED. Same tiers plus a ×1.2 modifier, a cap of 8,000 and a floor of
    /// 2,000: 2,400 × 1.2 = 2,880 and 3,600 × 1.2 = 4,320, neither touching cap or floor. This is the
    /// guard that the cache change did not reach into rate → modifier → cap → floor.
    /// </summary>
    [Fact]
    public async Task TheModifierCapAndFloorCascadeIsUnchanged()
    {
        var lab = await SeedAsync("Plan C - Cap y Floor", splitAtQuota: false, withCascade: true);
        await AddPendingAsync(lab, "C-VENTA-1", "C-VENTA-2");

        await RunJobAsync(lab);

        (await LedgerAmountsAsync(lab)).Should().BeEquivalentTo(new[] { 2880m, 4320m });
    }

    /// <summary>The job needs an <c>IBackgroundJobService</c> it never calls; this satisfies the
    /// constructor without reaching a real queue. Mirrors <c>ProcessPendingJobTests.NoOpJobService</c>,
    /// which is private to that file.</summary>
    private sealed class NoOpJobService : IBackgroundJobService
    {
        public Task<Guid> EnqueueAsync<TPayload>(TPayload payload, Guid tenantId, string userId, string userEmail, CancellationToken ct = default)
            where TPayload : notnull => Task.FromResult(Guid.NewGuid());
        public Task<JobStatusDto?> GetJobStatusAsync(Guid jobId, Guid tenantId, CancellationToken ct = default) => Task.FromResult<JobStatusDto?>(null);
        public Task UpdateProgressAsync(Guid jobId, int current, int total, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkRunningAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> CancelJobAsync(Guid jobId, Guid tenantId, CancellationToken ct = default) => Task.FromResult(true);
        public Task MarkCancelledAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetResultSummaryAsync(Guid jobId, string summaryJson, CancellationToken ct = default) => Task.CompletedTask;
    }
}
