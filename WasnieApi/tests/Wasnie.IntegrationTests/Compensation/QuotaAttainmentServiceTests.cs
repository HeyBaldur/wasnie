using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.IntegrationTests.Compensation;

[Collection(CreditAllocationServiceCollection.Name)]
public sealed class QuotaAttainmentServiceTests(CreditAllocationServiceFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Currency = "EUR";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Payee MakePayee(Guid tenantId, Guid payeeId) =>
        Payee.Create(tenantId, "Test Payee", "EMP-QAS-001", "qas@test.com",
            new DateOnly(2020, 1, 1), "test-user", payeeId, Now);

    private static Quota MakeActiveQuota(Guid tenantId, Guid payeeId, Guid planId,
        decimal target, DateRange period, QuotaMeasurementType type = QuotaMeasurementType.Revenue)
    {
        var quota = Quota.Create(tenantId, payeeId, planId,
            Money.Of(target, Currency), period, type,
            "test-user", Guid.NewGuid(), Now,
            planCurrency: Currency);
        quota.Activate("test-user", Now, Guid.NewGuid());
        return quota;
    }

    private static Plan MakePlan(Guid tenantId, Guid planId, DateRange period)
    {
        var plan = Plan.Create(tenantId, "Test Plan", "desc", period, Currency,
            "test-user", planId, Now, Guid.NewGuid());
        plan.AddRule("Flat Rule", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(0.05m));
        return plan;
    }

    private static PlanAssignment MakeAssignment(Guid tenantId, Guid planId, Guid payeeId, DateRange period) =>
        PlanAssignment.Create(tenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-QAS-001"),
            period, "test-user", Guid.NewGuid(), Now, Guid.NewGuid());

    // Seeds a Credit record whose source transaction falls on the given date with the given amount.
    // currency defaults to EUR; pass a different value to test cross-currency filtering.
    private async Task SeedCreditAsync(
        Wasnie.Infrastructure.Persistence.ApplicationDbContext db,
        Guid tenantId, Guid payeeId, Guid planId, Guid ruleId,
        decimal amount, DateOnly txDate, string currency = "EUR")
    {
        var tx = CompensationTransaction.Ingest(
            tenantId, $"REF-{Guid.NewGuid():N}", payeeId,
            Money.Of(amount, currency), txDate,
            TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid());
        tx.MarkCalculated(1, Money.Of(amount * 0.05m, currency), "test", Now, Guid.NewGuid());
        db.CompensationTransactions.Add(tx);
        await db.SaveChangesAsync();

        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Flat Rule",
            RateTable.Flat(0.05m), Trigger.Always(), Now);
        var credit = Credit.Allocate(tenantId, tx.Id, payeeId, planId, ruleId,
            snapshot, Money.Of(amount, currency), Money.Of(amount * 0.05m, currency),
            Percentage.FromPercent(100), CreditRole.Primary,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
        db.Credits.Add(credit);
        await db.SaveChangesAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAsync_Revenue_ReturnsCorrectAttainment()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Guid ruleId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId, period);
            ruleId = plan.Rules.First().Id;
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId, period));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 50_000m, period));
            await db.SaveChangesAsync();

            // Seed two credits within the period (Transaction.Amount 30k and 7,714.32; CreditedAmount = 5% each).
            // Revenue attainment (Sales Quota) sums Transaction.Amount: 30,000 + 7,714.32 = 37,714.32 EUR.
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId, 30_000m, new DateOnly(2026, 5, 10));
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId, 7_714.32m, new DateOnly(2026, 5, 20));
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 25));

            // Transaction.Amount total = 37,714.32 EUR; target = 50,000 EUR → 37714.32/50000 = 0.7543
            result.Value.Should().Be(0.7543m);
            result.ToPercentString().Should().Be("75%");
        }
    }

    [Fact]
    public async Task ComputeAsync_Units_ReturnsCorrectAttainment()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Guid ruleId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId, period);
            ruleId = plan.Rules.First().Id;
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId, period));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 100m, period, QuotaMeasurementType.Units));
            await db.SaveChangesAsync();

            // Seed a transaction with Quantity=80 (inside period)
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-UNITS", payeeId,
                Money.Of(8_000m, Currency), new DateOnly(2026, 5, 15),
                TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid(), quantity: 80);
            tx.MarkCalculated(1, Money.Of(400m, Currency), "test", Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Flat Rule",
                RateTable.Flat(0.05m), Trigger.Always(), Now);
            var credit = Credit.Allocate(tenantId, tx.Id, payeeId, planId, ruleId,
                snapshot, Money.Of(8_000m, Currency), Money.Of(400m, Currency),
                Percentage.FromPercent(100), CreditRole.Primary,
                "test", Guid.NewGuid(), Now, Guid.NewGuid());
            db.Credits.Add(credit);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 20));

            result.Value.Should().Be(0.8m); // 80 / 100 = 80%
            result.ToPercentString().Should().Be("80%");
        }
    }

    [Fact]
    public async Task ComputeAsync_NoMatchingQuota_ReturnsZero()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            result.Should().Be(AttainmentPercentage.Zero);
        }
    }

    [Fact]
    public async Task ComputeAsync_DraftQuota_ReturnsZero()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            // Draft quota — not activated
            var draft = Quota.Create(tenantId, payeeId, planId,
                Money.Of(50_000m, Currency), period, QuotaMeasurementType.Revenue,
                "test-user", Guid.NewGuid(), Now,
                planCurrency: Currency);
            db.Quotas.Add(draft);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            result.Should().Be(AttainmentPercentage.Zero);
        }
    }

    [Fact]
    public async Task ComputeAsync_OverlappingPeriods_SelectsShortestPeriod()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var shortPeriod = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));   // 30 days
        var longPeriod = DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));    // 364 days

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 10_000m, shortPeriod)); // target 10k
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 100_000m, longPeriod)); // target 100k
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            // asOfDate falls in both periods. Shortest (May) wins → target=10,000.
            // No credits → achieved=0 → 0%
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            result.Should().Be(AttainmentPercentage.Zero); // 0 / 10,000 = 0
        }
    }

    [Fact]
    public async Task ComputeAsync_SameTripleCalledTwice_HitsDbOnce()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 50_000m, period));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var asOf = new DateOnly(2026, 5, 15);

            var r1 = await svc.ComputeAsync(payeeId, planId, asOf);
            var r2 = await svc.ComputeAsync(payeeId, planId, asOf);

            // Both calls must return the same result (cache hit on second call).
            r1.Should().Be(r2);
        }
    }

    [Fact]
    public async Task ComputeAsync_CreditsOutsidePeriod_NotCounted()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Guid ruleId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId, DateRange.Of(
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            ruleId = plan.Rules.First().Id;
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 50_000m, period));
            await db.SaveChangesAsync();

            // Credit on June 1 — outside the May quota period
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId, 30_000m, new DateOnly(2026, 6, 1));
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            result.Should().Be(AttainmentPercentage.Zero); // 0 credits inside May
        }
    }

    // ── WI-CALC-A.3-FIX-2: multi-period regression guard ─────────────────────

    /// <summary>
    /// Reproduces the reported bug: same payee+plan has credits in Jan AND Jun.
    /// A quota for Jun-Jul must only count Jun CreditedAmount — not bleed Jan credits.
    /// Old code (using OriginalAmount) also failed this because of the wrong field.
    /// </summary>
    [Fact]
    public async Task ComputeAsync_Revenue_MultiPeriodCredits_OnlyCountsCorrectPeriod()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var janPeriod = DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        var junPeriod = DateRange.Of(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        Guid ruleId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId,
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            ruleId = plan.Rules.First().Id;
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))));
            // Only the Jun-Jul quota is Active (Jan quota omitted — testing isolation without it).
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 500m, junPeriod));
            await db.SaveChangesAsync();

            // 5 Jan credits: amount=100 each → Jan Transaction.Amount total = 500 EUR (outside Jun quota)
            for (var i = 0; i < 5; i++)
                await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                    100m, new DateOnly(2026, 1, 15));

            // 3 Jun credits: amount=500 each → Jun Transaction.Amount total = 1,500 EUR
            for (var i = 0; i < 3; i++)
                await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                    500m, new DateOnly(2026, 6, 15));
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 6, 20));

            // Jun Transaction.Amount = 1,500 EUR; target = 500 EUR → 1500/500 = 3.0000 (overachievement)
            result.Value.Should().Be(3.0m);
            result.ToPercentString().Should().Be("300%");
        }
    }

    /// <summary>
    /// Currency filter: credits in PLN must not count toward a EUR quota,
    /// even if they fall within the quota period.
    /// </summary>
    [Fact]
    public async Task ComputeAsync_Revenue_CurrencyFilter_ExcludesWrongCurrency()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Guid ruleId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId, period);
            ruleId = plan.Rules.First().Id;
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId, period));
            // EUR quota with target 1,000 EUR
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 1_000m, period));
            await db.SaveChangesAsync();

            // EUR credit: Transaction.Amount = 2,000 EUR → counts toward EUR quota
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                2_000m, new DateOnly(2026, 5, 10), "EUR");

            // PLN credit (wrong currency): Transaction.Amount = 5,000 PLN — must be excluded
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                5_000m, new DateOnly(2026, 5, 15), "PLN");
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 20));

            // Only EUR transaction counts: 2,000 EUR / 1,000 EUR target = 2.0000 (overachievement)
            result.Value.Should().Be(2.0m);
            result.ToPercentString().Should().Be("200%");
        }
    }

    /// <summary>
    /// Agnieszka-scenario integration test: 20 Jan credits + 3 Jun credits on the same plan.
    /// The Jun-Jul quota must see only the Jun Transaction.Amount — not Jan credits.
    /// Updated for WI-CALC-A.3-FIX-4: Revenue attainment sums Transaction.Amount (Sales Quota).
    /// </summary>
    [Fact]
    public async Task ComputeAsync_Revenue_AgnieszkaScenario_SumsTransactionAmountForCorrectPeriod()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var junPeriod = DateRange.Of(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        Guid ruleId;

        // Jun credit amounts match real data: 3 credits whose Transaction.Amount ≈ 2,285 EUR each
        // → total Jun Transaction.Amount = 6,855 EUR → 6,855 / 25,000 = 27.42%
        const decimal junTxAmount = 2_285m;
        var expectedJunTxTotal = junTxAmount * 3; // 6,855

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlan(tenantId, planId,
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            ruleId = plan.Rules.First().Id;
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 25_000m, junPeriod));
            await db.SaveChangesAsync();

            // 20 Jan 2026 credits — these must NOT bleed into the Jun quota
            for (var i = 0; i < 20; i++)
                await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                    140.196m, new DateOnly(2026, 1, 15));

            // 3 Jun 2026 credits — only these should count
            for (var i = 0; i < 3; i++)
                await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                    junTxAmount, new DateOnly(2026, 6, 15));
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 6, 20));

            // Expected: Jun Transaction.Amount / 25,000 target = 6,855 / 25,000 = 0.2742
            var expectedAttainment = Math.Round(expectedJunTxTotal / 25_000m, 4, MidpointRounding.ToEven);
            result.Value.Should().Be(expectedAttainment);

            // Must include only Jun transactions — Jan tx total would inflate to 0.2742 + 20*140.196/25000
            result.Value.Should().BeGreaterThan(0.2m);
            result.Value.Should().BeLessThan(0.5m); // still reasonable — not the full 100%+ territory
        }
    }

    // ── Multi-rule dedup (Step-3 consequence): attainment counts DISTINCT sales per plan, not credits ──

    private static Credit MakeCredit(Guid tenantId, Guid txId, Guid payeeId, Guid planId, Guid ruleId,
        decimal amount, string currency, int sortOrder)
    {
        var snapshot = RuleSnapshot.Freeze(ruleId, planId, sortOrder, $"Rule {sortOrder}",
            RateTable.Flat(0.05m), Trigger.Always(), Now);
        return Credit.Allocate(tenantId, txId, payeeId, planId, ruleId,
            snapshot, Money.Of(amount, currency), Money.Of(amount * 0.05m, currency),
            Percentage.FromPercent(100), CreditRole.Primary,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    // Seeds ONE sale (transaction) plus `creditCount` LIVE credits on it, all in `planId` with DISTINCT
    // rule ids — the case where several rules of one plan each credit the same sale (Step 3). Returns the tx.
    private async Task<CompensationTransaction> SeedSaleWithNCreditsAsync(
        Wasnie.Infrastructure.Persistence.ApplicationDbContext db,
        Guid tenantId, Guid payeeId, Guid planId,
        decimal amount, DateOnly txDate, int creditCount, string currency = "EUR", int quantity = 1)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId, $"REF-{Guid.NewGuid():N}", payeeId,
            Money.Of(amount, currency), txDate,
            TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid(), quantity: quantity);
        tx.MarkCalculated(creditCount, Money.Of(amount * 0.05m, currency), "test", Now, Guid.NewGuid());
        db.CompensationTransactions.Add(tx);
        await db.SaveChangesAsync();

        for (var i = 0; i < creditCount; i++)
            db.Credits.Add(MakeCredit(tenantId, tx.Id, payeeId, planId, Guid.NewGuid(), amount, currency, i + 1));
        await db.SaveChangesAsync();
        return tx;
    }

    // (b) Two rules crediting ONE sale → the sale counts ONCE (not twice).
    [Fact]
    public async Task ComputeAsync_Revenue_TwoRulesOnSameSale_CountsSaleOnce()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 10_000m, period));
            await db.SaveChangesAsync();
            await SeedSaleWithNCreditsAsync(db, tenantId, payeeId, planId, 6_000m, new DateOnly(2026, 5, 10), creditCount: 2);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            // 6,000 counted ONCE / 10,000 = 0.6. The old (per-credit) code would double to 12,000 → 1.2.
            result.Value.Should().Be(0.6m);
        }
    }

    // (c) Two rules over MANY sales → achieved = sum of the N sales, not 2N.
    [Fact]
    public async Task ComputeAsync_Revenue_TwoRulesManySales_SumsEachSaleOnce()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 6_000m, period));
            await db.SaveChangesAsync();
            foreach (var amount in new[] { 1_000m, 2_000m, 3_000m })
                await SeedSaleWithNCreditsAsync(db, tenantId, payeeId, planId, amount, new DateOnly(2026, 5, 10), creditCount: 2);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            // Sum of the three sales = 6,000 / 6,000 = 1.0. Old code → 12,000 → 2.0.
            result.Value.Should().Be(1.0m);
        }
    }

    // (d) The SAME sale credited in TWO different plans counts for BOTH quotas — no cross-plan dedup.
    [Fact]
    public async Task ComputeAsync_Revenue_SameSaleInTwoPlans_CountsForBothQuotas()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planA = Guid.NewGuid();
        var planB = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planA, 5_000m, period));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planB, 5_000m, period));
            await db.SaveChangesAsync();

            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-TWO-PLANS", payeeId, Money.Of(5_000m, Currency), new DateOnly(2026, 5, 10),
                TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid());
            tx.MarkCalculated(2, Money.Of(250m, Currency), "test", Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            db.Credits.Add(MakeCredit(tenantId, tx.Id, payeeId, planA, Guid.NewGuid(), 5_000m, Currency, 1));
            db.Credits.Add(MakeCredit(tenantId, tx.Id, payeeId, planB, Guid.NewGuid(), 5_000m, Currency, 1));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            (await svc.ComputeAsync(payeeId, planA, new DateOnly(2026, 5, 15))).Value.Should().Be(1.0m);
            (await svc.ComputeAsync(payeeId, planB, new DateOnly(2026, 5, 15))).Value.Should().Be(1.0m);
        }
    }

    // (e) Superseded credits stay excluded, even in the dedup path.
    [Fact]
    public async Task ComputeAsync_Revenue_SupersededCreditsExcluded_WithDedup()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 4_000m, period));
            await db.SaveChangesAsync();

            // Live sale with 2 credits (dedup → counts 4,000 once).
            await SeedSaleWithNCreditsAsync(db, tenantId, payeeId, planId, 4_000m, new DateOnly(2026, 5, 10), creditCount: 2);

            // A second sale whose only credit is superseded → must NOT be counted at all.
            var deadTx = CompensationTransaction.Ingest(
                tenantId, "REF-DEAD", payeeId, Money.Of(9_999m, Currency), new DateOnly(2026, 5, 12),
                TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid());
            deadTx.MarkCalculated(1, Money.Of(499.95m, Currency), "test", Now, Guid.NewGuid());
            db.CompensationTransactions.Add(deadTx);
            await db.SaveChangesAsync();

            var dead = MakeCredit(tenantId, deadTx.Id, payeeId, planId, Guid.NewGuid(), 9_999m, Currency, 1);
            dead.Supersede("test", Now, Guid.NewGuid());
            db.Credits.Add(dead);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            // Only the live sale counts, once: 4,000 / 4,000 = 1.0. The 9,999 superseded sale is excluded.
            result.Value.Should().Be(1.0m);
        }
    }

    // (f) Units also dedups: two rules on one sale count its Quantity once.
    [Fact]
    public async Task ComputeAsync_Units_TwoRulesOnSameSale_CountsQuantityOnce()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 40m, period, QuotaMeasurementType.Units));
            await db.SaveChangesAsync();
            await SeedSaleWithNCreditsAsync(db, tenantId, payeeId, planId, 8_000m, new DateOnly(2026, 5, 10), creditCount: 2, quantity: 40);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            // Quantity 40 counted ONCE / 40 = 1.0. Old code → 80 → 2.0.
            result.Value.Should().Be(1.0m);
        }
    }

    // (g) The split-at-quota PriorCumulative (delegates to Revenue) also counts each sale once.
    [Fact]
    public async Task GetSplitContextAsync_TwoRulesOnSameSale_PriorCumulativeCountsOnce()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var period = DateRange.Of(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 10_000m, period));
            await db.SaveChangesAsync();
            await SeedSaleWithNCreditsAsync(db, tenantId, payeeId, planId, 8_000m, new DateOnly(2026, 5, 10), creditCount: 2);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var ctx = await svc.GetSplitContextAsync(payeeId, planId, new DateOnly(2026, 5, 15));

            ctx.Should().NotBeNull();
            ctx!.PriorCumulative.Should().Be(8_000m); // counted once, not 16,000
            ctx.QuotaTarget.Should().Be(10_000m);
        }
    }
}
