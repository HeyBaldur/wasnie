using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// KAN-87 — THE SECOND SALE OF A DAY IS PRICED WITH THE ATTAINMENT THE FIRST ONE LEFT BEHIND.
///
/// ★★ THESE TESTS WERE WRITTEN AGAINST THE DEFECT AND NOW GUARD THE FIX. They first recorded what the
/// engine did wrong — the second sale of a day reading 0.90 when the truth was 1.20 — and were flipped
/// when the cache was removed. They are the reason a cache cannot quietly come back: any memoisation
/// that survives a committed credit turns the first assertion red.
///
/// ★★ WHY THIS IS MONEY. <c>QuotaAttainmentService.ComputeAsync</c> used to memoise per
/// <c>(payeeId, planId, asOfDate)</c> for the life of the instance, and the instance lives for the
/// whole background job: Hangfire opens one scope per job (<c>HangfireJobDispatcher.cs:45</c>) and the
/// service is registered Scoped (<c>DependencyInjection.cs:316</c>). The job commits each transaction
/// before moving to the next (<c>ProcessPendingTransactionsJobHandler.cs:221</c>), so by the time the
/// second sale of a day is evaluated its sibling's credit IS in the database — and the cache used to
/// hand back the reading from before it. On tiers of 8% below quota and 12% above, that made a 30,000
/// second sale worth 2,400 or 3,600 depending on nothing but whether the two sales were processed in
/// one run.
///
/// ★ THE TWO HALVES OF THE SAME FEATURE DISAGREED, which is what showed this was an oversight rather
/// than a policy: <c>GetSplitContextAsync</c> has never cached, and says why — "PriorCumulative changes
/// after each transaction is committed to DB". Split ON was already right; Split OFF was the stale one.
/// The test below still asserts the split path, so the fix cannot have broken the half that worked.
///
/// ★ ON THE PROVIDER: this runs on EF InMemory, and that is not a weakness here. What was under test
/// is a <c>Dictionary</c> inside the service, which no database provider can influence; the database's
/// only job is to hold the true value. The end-to-end proof over real SQL Server, driving the actual
/// job handler, lives in the integration suite (<c>Kan87AttainmentCacheJobTests</c>).
/// </summary>
public sealed class Kan87AttainmentCacheTests
{
    private const string EUR = "EUR";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PayeeId = Guid.NewGuid();
    private static readonly Guid PlanId = Guid.NewGuid();

    /// <summary>The day both sales of the run fall on — one cache key for the two of them, as was.</summary>
    private static readonly DateOnly SaleDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext BuildDb()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());
    }

    /// <summary>
    /// A sale AND a live credit for it. Both halves matter: <c>QuotaAchievedQuery</c> counts a
    /// transaction only when it has an unsuperseded credit in that (payee, plan), so a sale without
    /// one would not move attainment and the test would prove nothing.
    /// </summary>
    private static async Task CommitSaleWithCreditAsync(
        ApplicationDbContext db, decimal amount, DateOnly date)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId: TenantId, referenceNumber: $"S-{amount}", payeeId: PayeeId,
            amount: Money.Of(amount, EUR), transactionDate: date, source: TransactionSource.Manual,
            ingestedBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid(), quantity: 1);

        var snapshot = RuleSnapshot.Freeze(
            Guid.NewGuid(), PlanId, 1, "R", RateTable.Flat(0.08m), Trigger.Always(), Now,
            measurement: new Measurement { Type = MeasurementType.Revenue });

        var credit = Credit.Allocate(
            tenantId: TenantId, transactionId: tx.Id, payeeId: PayeeId, planId: PlanId,
            ruleId: Guid.NewGuid(), ruleSnapshot: snapshot,
            originalAmount: Money.Of(amount, EUR),
            creditedAmount: Money.Of(amount * 0.08m, EUR),
            splitPercentage: Percentage.FromPercent(100m), role: CreditRole.Primary,
            allocatedBy: "test", id: Guid.NewGuid(), now: Now, eventId: Guid.NewGuid());

        db.CompensationTransactions.Add(tx);
        db.Credits.Add(credit);
        await db.SaveChangesAsync();
    }

    /// <summary>A quota of 100,000 in force all year, with 90,000 already credited before the run.</summary>
    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = BuildDb();

        var quota = Quota.Create(
            tenantId: TenantId, payeeId: PayeeId, planId: PlanId,
            amount: Money.Of(100000m, EUR),
            period: DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            measurementType: QuotaMeasurementType.Revenue,
            createdBy: "test", id: Guid.NewGuid(), now: Now);
        quota.Activate("test", Now, Guid.NewGuid());
        db.Quotas.Add(quota);
        await db.SaveChangesAsync();

        await CommitSaleWithCreditAsync(db, 90000m, new DateOnly(2026, 2, 1));
        return db;
    }

    /// <summary>
    /// ★★ THE FIX, GUARDED. One service instance — which is what a job run has — asked twice for the
    /// same (payee, plan, date) with a committed 30,000 sale in between now answers 1.20, the true
    /// attainment at that moment. Before the fix it answered 0.90, the reading from before its sibling
    /// existed.
    ///
    /// THIS IS THE ASSERTION THAT BREAKS IF MEMOISATION RETURNS. Any cache keyed on the date survives a
    /// committed credit, and this test is what notices.
    /// </summary>
    [Fact]
    public async Task TheSecondSaleOfTheDaySeesTheAttainmentTheFirstOneLeft()
    {
        using var db = await SeedAsync();
        var service = new QuotaAttainmentService(db);

        var beforeTheRun = await service.ComputeAsync(PayeeId, PlanId, SaleDate);
        beforeTheRun.Value.Value.Should().Be(0.9m);

        // Sale 1 of the run is processed and committed, exactly as the job does per transaction.
        await CommitSaleWithCreditAsync(db, 30000m, SaleDate);

        var forTheSecondSale = await service.ComputeAsync(PayeeId, PlanId, SaleDate);

        forTheSecondSale.Value.Value.Should().Be(
            1.2m, "the 30,000 sibling is committed, so the second sale is above quota");
    }

    /// <summary>
    /// ★★ THE SPLIT PATH WAS ALREADY RIGHT AND MUST STAY RIGHT. It is the control: it never cached, so
    /// removing the bracket cache must not have touched it. It still reports 120,000.
    /// </summary>
    [Fact]
    public async Task TheSplitPathIsCorrectInTheVerySameSequence()
    {
        using var db = await SeedAsync();
        var service = new QuotaAttainmentService(db);

        await service.ComputeAsync(PayeeId, PlanId, SaleDate);
        (await service.GetSplitContextAsync(PayeeId, PlanId, SaleDate))!.PriorCumulative.Should().Be(90000m);

        await CommitSaleWithCreditAsync(db, 30000m, SaleDate);

        var split = await service.GetSplitContextAsync(PayeeId, PlanId, SaleDate);
        split!.PriorCumulative.Should().Be(120000m, "GetSplitContextAsync does not cache, on purpose");
    }

    /// <summary>
    /// ★ THE CACHE KEY WAS THE CULPRIT, NOT THE DATABASE. Even before the fix, the same instance asked
    /// for a DIFFERENT date read 1.20 — the fresh value was there all along and only the key collision
    /// hid it, which is why the defect needed two sales on the SAME DAY to appear. Kept as a control.
    /// </summary>
    [Fact]
    public async Task ADifferentDateOnTheSameInstanceSeesTheFreshValue()
    {
        using var db = await SeedAsync();
        var service = new QuotaAttainmentService(db);

        await service.ComputeAsync(PayeeId, PlanId, SaleDate);
        await CommitSaleWithCreditAsync(db, 30000m, SaleDate);

        var nextDay = await service.ComputeAsync(PayeeId, PlanId, SaleDate.AddDays(1));

        nextDay.Value.Value.Should().Be(1.2m);
    }

    /// <summary>
    /// ★★ DETERMINISM, WHICH IS THE PROPERTY THAT WAS ACTUALLY BROKEN. A second instance — which is
    /// what a SEPARATE run is — must reach the same attainment as the first instance does inside one
    /// run. Before the fix these were 0.90 and 1.20: the same sale was worth 2,400 or 3,600 depending
    /// on how the transactions happened to be grouped into runs.
    /// </summary>
    [Fact]
    public async Task SameDataGivesTheSameAttainmentWhetherSplitAcrossRunsOrNot()
    {
        using var db = await SeedAsync();

        var runOne = new QuotaAttainmentService(db);
        await runOne.ComputeAsync(PayeeId, PlanId, SaleDate);
        await CommitSaleWithCreditAsync(db, 30000m, SaleDate);

        var inTheSameRun = (await runOne.ComputeAsync(PayeeId, PlanId, SaleDate)).Value.Value;
        var inASeparateRun = (await new QuotaAttainmentService(db).ComputeAsync(PayeeId, PlanId, SaleDate)).Value.Value;

        inTheSameRun.Should().Be(inASeparateRun, "the same sale must not be worth two different amounts");
        inTheSameRun.Should().Be(1.2m);
    }
}
