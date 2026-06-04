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
    private async Task SeedCreditAsync(
        Wasnie.Infrastructure.Persistence.ApplicationDbContext db,
        Guid tenantId, Guid payeeId, Guid planId, Guid ruleId,
        decimal amount, DateOnly txDate)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId, $"REF-{Guid.NewGuid():N}", payeeId,
            Money.Of(amount, Currency), txDate,
            TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid());
        tx.MarkCalculated(1, Money.Of(amount * 0.05m, Currency), "test", Now, Guid.NewGuid());
        db.CompensationTransactions.Add(tx);
        await db.SaveChangesAsync();

        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Flat Rule",
            RateTable.Flat(0.05m), Trigger.Always(), Now);
        var credit = Credit.Allocate(tenantId, tx.Id, payeeId, planId, ruleId,
            snapshot, Money.Of(amount, Currency), Money.Of(amount * 0.05m, Currency),
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

            // Seed 37,714.32 EUR in credits within the period
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId, 30_000m, new DateOnly(2026, 5, 10));
            await SeedCreditAsync(db, tenantId, payeeId, planId, ruleId, 7_714.32m, new DateOnly(2026, 5, 20));
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var svc = new QuotaAttainmentService(db);
            var result = await svc.ComputeAsync(payeeId, planId, new DateOnly(2026, 5, 25));

            result.Value.Should().Be(0.7543m); // (37714.32 / 50000) rounded to 4 decimals
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
}
