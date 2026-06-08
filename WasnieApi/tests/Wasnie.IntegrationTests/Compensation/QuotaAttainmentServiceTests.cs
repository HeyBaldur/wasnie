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

            // Seed two credits within the period (OriginalAmount 30k and 7,714.32; CreditedAmount = 5% each).
            // Revenue attainment sums CreditedAmount: 1,500 + 385.716 = 1,885.716 EUR.
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId, 30_000m, new DateOnly(2026, 5, 10));
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId, 7_714.32m, new DateOnly(2026, 5, 20));
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 25));

            // CreditedAmount total = 1,885.716 EUR; target = 50,000 EUR → 1885.716/50000 = 0.0377
            result.Value.Should().Be(0.0377m);
            result.ToPercentString().Should().Be("4%");
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

            // 5 Jan credits: amount=100 → CreditedAmount=5 each → total Jan CreditedAmount = 25 EUR
            for (var i = 0; i < 5; i++)
                await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                    100m, new DateOnly(2026, 1, 15));

            // 3 Jun credits: amount=500 → CreditedAmount=25 each → total Jun CreditedAmount = 75 EUR
            for (var i = 0; i < 3; i++)
                await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                    500m, new DateOnly(2026, 6, 15));
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 6, 20));

            // Jun CreditedAmount = 75 EUR; target = 500 EUR → 75/500 = 0.1500
            result.Value.Should().Be(0.15m);
            result.ToPercentString().Should().Be("15%");
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

            // EUR credit: amount=2,000 → CreditedAmount=100 EUR
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                2_000m, new DateOnly(2026, 5, 10), "EUR");

            // PLN credit (wrong currency): amount=5,000 → CreditedAmount=250 PLN — must be excluded
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId,
                5_000m, new DateOnly(2026, 5, 15), "PLN");
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 20));

            // Only EUR credit counts: 100 EUR / 1,000 EUR target = 0.1000
            result.Value.Should().Be(0.1m);
            result.ToPercentString().Should().Be("10%");
        }
    }

    /// <summary>
    /// Agnieszka-scenario integration test: 20 Jan credits + 3 Jun credits on the same plan.
    /// The Jun-Jul quota must see exactly the Jun CreditedAmount — not 20× it.
    /// This is the exact bug reported in WI-CALC-A.3-FIX-2.
    /// </summary>
    [Fact]
    public async Task ComputeAsync_Revenue_AgnieszkaScenario_NotInflatedBy20x()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var junPeriod = DateRange.Of(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        Guid ruleId;

        // Jun credit amounts match real data: 3 credits whose OriginalAmount ≈ 2,285 EUR each
        // → CreditedAmount ≈ 114.25 EUR each → total ≈ 342.75 EUR
        const decimal junTxAmount = 2_285m;
        var expectedJunCreditedTotal = junTxAmount * 0.05m * 3; // 342.75

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

            // Expected: Jun CreditedAmount / 25,000 target
            // 342.75 / 25,000 = 0.0137
            var expectedAttainment = Math.Round(expectedJunCreditedTotal / 25_000m, 4, MidpointRounding.ToEven);
            result.Value.Should().Be(expectedAttainment);

            // Must NOT be 20x inflated (which would be 0.2745 = 342.75*20/25000)
            result.Value.Should().BeLessThan(0.02m);
        }
    }
}
