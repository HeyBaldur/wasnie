using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// WI-CALC-A.1: Unit-style integration tests for CreditAllocationService.
/// Uses a real Testcontainers SQL Server so EF Core owned-type DateRange comparisons work correctly.
/// </summary>
[Collection(CreditAllocationServiceCollection.Name)]
public sealed class CreditAllocationServiceTests(CreditAllocationServiceFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly string Currency = "EUR";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Payee MakePayee(Guid tenantId, Guid? payeeId = null) =>
        Payee.Create(tenantId, "Test Payee", "EMP-TEST-001", "test@test.com",
            new DateOnly(2020, 1, 1), "test-user",
            payeeId ?? Guid.NewGuid(), Now);

    private static Plan MakePlanWithFlatRule(Guid tenantId, Guid planId, decimal rate,
        DateOnly planStart, DateOnly planEnd, DateOnly? ruleStart = null, DateOnly? ruleEnd = null)
    {
        var plan = Plan.Create(tenantId, "Test Plan", "desc",
            DateRange.Of(planStart, planEnd),
            Currency, "test-user", planId, Now, Guid.NewGuid());

        var effectivePeriod = (ruleStart.HasValue && ruleEnd.HasValue)
            ? DateRange.Of(ruleStart.Value, ruleEnd.Value)
            : null;

        plan.AddRule("Base Commission", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(rate),
            effectivePeriod: effectivePeriod);

        return plan;
    }

    private static PlanAssignment MakeAssignment(Guid tenantId, Guid planId, Guid payeeId,
        DateOnly start, DateOnly end)
    {
        var payeeRef = PayeeReference.Snapshot(payeeId, "Test Payee", "EMP-TEST-001");
        return PlanAssignment.Create(tenantId, planId, payeeId, payeeRef,
            DateRange.Of(start, end), "test-user", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    private CreditAllocationService CreateService(CreditAllocationServiceFixture.FixedTenantContext ctx,
        Wasnie.Infrastructure.Persistence.ApplicationDbContext db,
        IQuotaAttainmentService? attainmentService = null)
    {
        var clock = new FakeClock(Now.UtcDateTime);
        var guidGen = new FakeGuidGenerator();
        return new CreditAllocationService(db, guidGen, clock,
            NullLogger<CreditAllocationService>.Instance,
            attainmentService ?? new StubQuotaAttainmentService());
    }

    private static Quota MakeActiveQuota(Guid tenantId, Guid payeeId, Guid planId,
        decimal target, DateRange period)
    {
        var quota = Quota.Create(tenantId, payeeId, planId,
            Money.Of(target, Currency), period, QuotaMeasurementType.Revenue,
            "test-user", Guid.NewGuid(), Now, planCurrency: Currency);
        quota.Activate("test-user", Now, Guid.NewGuid());
        return quota;
    }

    // ── Core allocation ───────────────────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_WithFlatRule_ReturnsOneCredit()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithFlatRule(tenantId, planId, rate: 0.10m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-001", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(100m); // 1000 * 0.10
            credits[0].CreditedAmount.Currency.Should().Be(Currency);
            credits[0].PayeeId.Should().Be(payeeId);
            credits[0].PlanId.Should().Be(planId);
            credits[0].Role.Should().Be(CreditRole.Primary);
        }
    }

    [Fact]
    public async Task AllocateAsync_NullPayeeId_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();

        await using var db = fixture.CreateDbForTenant(tenantId);
        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-NOPAYEE", null, Money.Of(1000m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().BeEmpty();
    }

    [Fact]
    public async Task AllocateAsync_NotPendingTransaction_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithFlatRule(tenantId, planId, rate: 0.10m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-ELIGIBLE", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());
            tx.MarkEligible("validator", Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AllocateAsync_NoAssignment_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        await using var db = fixture.CreateDbForTenant(tenantId);
        var payee = MakePayee(tenantId, payeeId);
        db.Payees.Add(payee);
        await db.SaveChangesAsync();

        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-NOASSIGN", payeeId, Money.Of(1000m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().BeEmpty();
    }

    [Fact]
    public async Task AllocateAsync_AssignmentDoesNotCoverTxDate_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithFlatRule(tenantId, planId, rate: 0.10m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            // Assignment covers Q1 2025 — transaction date is 2026-03-15, not covered.
            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-NOCOVER", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AllocateAsync_TieredRate_ComputesCorrectCommission()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // Tier 1: 0-500 at 5%, Tier 2: 500+ at 10%.
            var tiers = new List<RateTier>
            {
                new() { From = 0m, To = 500m, Rate = 0.05m },
                new() { From = 500m, To = null, Rate = 0.10m }
            };
            var plan = Plan.Create(tenantId, "Tiered Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Tiered Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Tiered(tiers));
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // 800 total: 500 * 0.05 = 25, 300 * 0.10 = 30 → total 55.
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-TIER", payeeId, Money.Of(800m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(55m);
        }
    }

    [Fact]
    public async Task AllocateAsync_CapPerTransaction_CapsCommission()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var cap = new Cap { Amount = Money.Of(50m, Currency), Scope = CapScope.PerTransaction };
            var plan = Plan.Create(tenantId, "Capped Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Capped Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.10m), cap: cap);
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // 10% of 1000 = 100, but cap is 50.
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-CAP", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(50m);
        }
    }

    [Fact]
    public async Task AllocateAsync_FloorApplied_WhenCommissionBelowFloor()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var floor = new Floor { Amount = Money.Of(20m, Currency) };
            var plan = Plan.Create(tenantId, "Floor Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Floor Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.01m), floor: floor);
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // 1% of 100 = 1, but floor is 20.
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-FLOOR", payeeId, Money.Of(100m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(20m);
        }
    }

    [Fact]
    public async Task AllocateAsync_RuleEffectivePeriodDoesNotCoverTxDate_RuleSkipped()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // Rule only active Jan-Feb 2026; tx date is Mar 15 2026.
            var plan = MakePlanWithFlatRule(tenantId, planId, rate: 0.10m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
                ruleStart: new DateOnly(2026, 1, 1), ruleEnd: new DateOnly(2026, 2, 28));
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-RULE-EXPIRED", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().BeEmpty("rule EffectivePeriod does not cover the transaction date");
        }
    }

    // Decision #65 (WI-CALC-MULTIPLAN-CURRENCY-MATCH, Pattern B): currency mismatch is now a routing
    // signal, not an error. A transaction whose currency doesn't match any plan stays Pending with
    // zero credits — no DomainException is thrown.
    [Fact]
    public async Task AllocateAsync_CurrencyMismatch_ReturnsEmptyCredits()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // Plan is in PLN, transaction in EUR — no currency match.
            var plan = Plan.Create(tenantId, "PLN Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "PLN", "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Base", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.10m));
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-CCY", payeeId, Money.Of(1000m, "EUR"),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);

            var credits = await svc.AllocateAsync(tx);

            credits.Should().BeEmpty("currency mismatch routes the transaction to Pending, not an error (Pattern B)");
        }
    }

    [Fact]
    public async Task AllocateAsync_RuleSnapshot_FreezesCopiesCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithFlatRule(tenantId, planId, rate: 0.05m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-SNAP", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            var snapshot = credits[0].RuleSnapshot;
            snapshot.PlanId.Should().Be(planId);
            snapshot.PlanVersion.Should().Be(1);
            snapshot.RuleName.Should().Be("Base Commission");
        }
    }

    // ── Trigger evaluation ────────────────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_TriggerAlways_AlwaysProducesCredit()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Always Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Always Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.05m), trigger: Trigger.Always());
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-ALWAYS", payeeId, Money.Of(500m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task AllocateAsync_TriggerAmountGreaterThan_SatisfiedCondition_ProducesCredit()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var trigger = Trigger.When(LogicalOperator.And,
                [new Condition
                {
                    Field = "TransactionAmount",
                    Operator = ConditionOperator.GreaterThan,
                    Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
                }]);

            var plan = Plan.Create(tenantId, "Trigger Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Conditional Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.10m), trigger: trigger);
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // Amount 1000 > 500 → condition met.
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-TRIGGER-OK", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task AllocateAsync_TriggerAmountGreaterThan_UnSatisfiedCondition_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var trigger = Trigger.When(LogicalOperator.And,
                [new Condition
                {
                    Field = "TransactionAmount",
                    Operator = ConditionOperator.GreaterThan,
                    Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
                }]);

            var plan = Plan.Create(tenantId, "Trigger Plan 2", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Conditional Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.10m), trigger: trigger);
            db.CompensationPlans.Add(plan);

            var payee = MakePayee(tenantId, payeeId);
            db.Payees.Add(payee);

            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // Amount 200 is NOT > 500 → condition not met.
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-TRIGGER-MISS", payeeId, Money.Of(200m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().BeEmpty();
        }
    }

    // ── End-to-end: ingest → MarkCalculated ──────────────────────────────────

    [Fact]
    public async Task AllocateAsync_FullFlow_CreditsAndMarkCalculatedRoundTrip()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithFlatRule(tenantId, planId, rate: 0.05m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        CompensationTransaction tx;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            tx = CompensationTransaction.Ingest(
                tenantId, "REF-E2E", payeeId, Money.Of(2000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            var credit = credits[0];
            credit.CreditedAmount.Amount.Should().Be(100m); // 2000 * 0.05

            foreach (var c in credits) db.Credits.Add(c);

            var total = credits[0].CreditedAmount;
            tx.MarkCalculated(credits.Count, total, "engine", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        // Re-load and verify persisted state.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var loadedTx = await db.CompensationTransactions
                .FindAsync(tx.Id);
            loadedTx!.Status.Should().Be(CompensationTransactionStatus.Calculated);

            var loadedCredits = await db.Credits
                .Where(c => c.TransactionId == tx.Id)
                .ToListAsync();
            loadedCredits.Should().HaveCount(1);
            loadedCredits[0].CreditedAmount.Amount.Should().Be(100m);
        }
    }

    [Fact]
    public async Task AllocateAsync_PlanHasNoActiveRules_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "No Rules Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            var rule = plan.AddRule("Deactivated Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.10m));
            plan.RemoveRule(rule.Id);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-NORULES", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().BeEmpty("plan has no active rules");
        }
    }

    [Fact]
    public async Task AllocateAsync_TwoMatchingRules_ReturnsTwoCreditsWithCorrectAmounts()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Two Rule Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Rule A 5pct", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.05m));
            plan.AddRule("Rule B 3pct", 2,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.03m));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-TWORULES", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(2);
            credits.Should().Contain(c => c.CreditedAmount.Amount == 50m);   // 1000 * 5%
            credits.Should().Contain(c => c.CreditedAmount.Amount == 30m);   // 1000 * 3%
            credits.Should().AllSatisfy(c => c.Role.Should().Be(CreditRole.Primary));
            credits.Should().AllSatisfy(c => c.SplitPercentage.Value.Should().Be(1m)); // Percentage.Value is 0-1 fraction
        }
    }

    [Fact]
    public async Task AllocateAsync_AttainmentBased_UsesRealAttainmentFromService()
    {
        // Stub returns 75% attainment → picks the 50-99% bracket at 7%.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tiers = new List<AttainmentTier>
            {
                new() { AttainmentFrom = 0m, AttainmentTo = 0.49m, Rate = 0.03m },
                new() { AttainmentFrom = 0.50m, AttainmentTo = 0.99m, Rate = 0.07m },
                new() { AttainmentFrom = 1.00m, AttainmentTo = null, Rate = 0.12m }
            };
            var plan = Plan.Create(tenantId, "Attainment Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Attainment Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.AttainmentBased(tiers));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-ATTAIN", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            // Stub: 75% attainment → 50-99% bracket → 7%
            var attainmentStub = new StubQuotaAttainmentService(
                AttainmentPercentage.FromAchievedAndTarget(75m, 100m));
            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db, attainmentStub);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(70m); // 1000 * 7%
            attainmentStub.CallCount.Should().Be(1); // computed exactly once
        }
    }

    [Fact]
    public async Task AllocateAsync_FlatPlan_DoesNotCallAttainmentService()
    {
        // Short-circuit: flat plans never call IQuotaAttainmentService.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithFlatRule(tenantId, planId, 0.05m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-FLAT", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var attainmentStub = new StubQuotaAttainmentService();
            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db, attainmentStub);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(50m); // 1000 * 5%
            attainmentStub.CallCount.Should().Be(0); // short-circuit: never called for flat plans
        }
    }

    [Fact]
    public async Task AllocateAsync_ModifierApplied_MultipliesCommission()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var modifier = new Modifier { Type = ModifierType.Multiplier, Factor = 1.5m, Name = "Bonus x1.5" };
            var plan = Plan.Create(tenantId, "Modifier Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Modifier Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.10m), modifier: modifier);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // 1000 * 10% = 100, then * 1.5 modifier = 150.
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-MODIFIER", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(150m); // 100 * 1.5
        }
    }

    [Fact]
    public async Task AllocateAsync_UnassignedTx_NoCreditCreated_StatusRemainsPending_E2E()
    {
        var tenantId = Guid.NewGuid();

        CompensationTransaction tx;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            tx = CompensationTransaction.Ingest(
                tenantId, "REF-UNASSIGNED-E2E", null, Money.Of(500m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().BeEmpty();
            // No MarkCalculated called → status remains Pending.
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var loadedTx = await db.CompensationTransactions.FindAsync(tx.Id);
            loadedTx!.Status.Should().Be(CompensationTransactionStatus.Pending);
            (await db.Credits.CountAsync(c => c.TransactionId == tx.Id)).Should().Be(0);
        }
    }

    // ── Split-at-quota ────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<AttainmentTier> EuAcceleratorTiers =
    [
        new() { AttainmentFrom = 0m, AttainmentTo = 1.0m, Rate = 0.04m },
        new() { AttainmentFrom = 1.0m, AttainmentTo = null, Rate = 0.07m }
    ];

    [Fact]
    public async Task AllocateAsync_SplitAtQuota_FlagSurvivesEfCoreRoundTrip()
    {
        // Regression guard: splitAtQuota=true must survive the EF Core JSON (nvarchar(max))
        // round-trip. This is the exact gap the previous WI's unit test skipped — it called
        // ComputeAttainmentSplitCommission directly in memory and never touched the DB.
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Split Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Split Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.AttainmentBased(EuAcceleratorTiers, splitAtQuota: true));
            db.CompensationPlans.Add(plan);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var loadedPlan = await db.CompensationPlans
                .IgnoreQueryFilters()
                .Include(p => p.Rules)
                .FirstAsync(p => p.Id == planId);

            loadedPlan.Rules.Single().RateTable.SplitAtQuota.Should().BeTrue(
                "splitAtQuota=true must survive the EF Core JSON serialization round-trip");
        }
    }

    [Fact]
    public async Task AllocateAsync_SplitAtQuota_DispatchCallsGetSplitContext_NotComputeAsync()
    {
        // Dispatch test: with SplitAtQuota=true, the engine routes to GetSplitContextAsync
        // (split path), not ComputeAsync (bracket path). Uses a stub that returns a known
        // context so the result proves which path executed.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Split Dispatch Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Split Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.AttainmentBased(EuAcceleratorTiers, splitAtQuota: true));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // prior=0, quota=250,000: tx of 100,000 → entirely below quota → 4% via split path
            var splitStub = new StubQuotaAttainmentService(
                splitContext: new AttainmentSplitContext(0m, 250_000m));

            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-DISPATCH", payeeId, Money.Of(100_000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db, splitStub);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(4_000m, "100,000 × 4% (below quota, split path)");
            splitStub.CallCount.Should().Be(0, "split path calls GetSplitContextAsync, not ComputeAsync");
        }
    }

    [Fact]
    public async Task AllocateAsync_SplitAtQuota_CasoAdrian_CommissionSplitsAtQuotaBoundary()
    {
        // Full integration: plan with splitAtQuota=true + real Quota + real QuotaAttainmentService.
        // Adrian: quota €250,000, single tx €277,880.25, no prior credits.
        // Expected: €250,000 × 4% + €27,880.25 × 7% = €10,000 + €1,951.6175 = €11,951.6175
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var q2Start = new DateOnly(2026, 4, 1);
        var q2End = new DateOnly(2026, 6, 30);
        var txDate = new DateOnly(2026, 4, 7);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "EU Accelerator Q2", "desc",
                DateRange.Of(q2Start, q2End), Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Accelerator Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.AttainmentBased(EuAcceleratorTiers, splitAtQuota: true));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId, q2Start, q2End));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 250_000m, DateRange.Of(q2Start, q2End)));
            await db.SaveChangesAsync();
        }

        await using var svcDb = fixture.CreateDbForTenant(tenantId);
        await using var attDb = fixture.CreateDbForTenant(tenantId);
        var realAttainment = new QuotaAttainmentService(attDb);

        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-ADRIAN-SPLIT", payeeId, Money.Of(277_880.25m, Currency),
            txDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb, realAttainment);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().HaveCount(1);
        credits[0].CreditedAmount.Amount.Should().Be(11_951.6175m,
            "€250,000 × 4% + €27,880.25 × 7% = €10,000 + €1,951.6175 = €11,951.6175");
        credits[0].CreditedAmount.Currency.Should().Be(Currency);
    }

    // ── Chronological ordering guarantee (F-4) ───────────────────────────────

    /// <summary>
    /// Demonstrates that PriorCumulative only accumulates correctly when transactions
    /// are processed in strict ascending TransactionDate order.
    ///
    /// Setup: quota €250k, two transactions:
    ///   tx1 (Apr 1, €260k) — should be processed first  → prior=0   → €250k×4% + €10k×7% = €10,700
    ///   tx2 (Jun 1, €50k)  — should be processed second → prior=260k → all above quota    →  €3,500
    ///
    /// When the LATER transaction (tx2, Jun 1) is mistakenly processed FIRST (simulating
    /// the bug where the job has no OrderBy), PriorCumulative for tx2 = 0 and for tx1 = 50k,
    /// producing wrong individual credits: tx2=€2,000 (wrong) and tx1=€12,200 (wrong).
    ///
    /// This test processes them in correct date order (tx1 then tx2) and verifies the split.
    /// The companion test below processes them in wrong order to prove ordering matters.
    /// The fix (OrderBy in ProcessPendingTransactionsJobHandler) ensures correct date order.
    /// </summary>
    [Fact]
    public async Task AllocateAsync_SplitAtQuota_CorrectDateOrder_ProducesCorrectPerTxSplit()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var q2Start = new DateOnly(2026, 4, 1);
        var q2End = new DateOnly(2026, 6, 30);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Order Test Plan", "desc",
                DateRange.Of(q2Start, q2End), Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Accelerator Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.AttainmentBased(EuAcceleratorTiers, splitAtQuota: true));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId, q2Start, q2End));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 250_000m, DateRange.Of(q2Start, q2End)));
            await db.SaveChangesAsync();
        }

        // Process tx1 (Apr 1, €260k) FIRST (correct chronological order).
        // prior=0 → [0, 260k] → €250k×4% + €10k×7% = €10,000 + €700 = €10,700
        await using (var svcDb = fixture.CreateDbForTenant(tenantId))
        await using (var attDb = fixture.CreateDbForTenant(tenantId))
        {
            var tx1 = CompensationTransaction.Ingest(
                tenantId, "ORDER-TX1-APR", payeeId, Money.Of(260_000m, Currency),
                new DateOnly(2026, 4, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            svcDb.CompensationTransactions.Add(tx1);

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb,
                new QuotaAttainmentService(attDb));
            var credits1 = await svc.AllocateAsync(tx1);
            credits1.Should().HaveCount(1);
            credits1[0].CreditedAmount.Amount.Should().Be(10_700m,
                "€250k×4% + €10k×7% = €10,000 + €700 (tx1 below quota then crosses it)");

            foreach (var c in credits1) svcDb.Credits.Add(c);
            tx1.MarkCalculated(1, credits1[0].CreditedAmount, "engine", Now, Guid.NewGuid());
            await svcDb.SaveChangesAsync();
        }

        // Process tx2 (Jun 1, €50k) SECOND.
        // prior=260k → [260k, 310k] → all above quota → all at 7% = €50k × 7% = €3,500
        await using (var svcDb = fixture.CreateDbForTenant(tenantId))
        await using (var attDb = fixture.CreateDbForTenant(tenantId))
        {
            var tx2 = CompensationTransaction.Ingest(
                tenantId, "ORDER-TX2-JUN", payeeId, Money.Of(50_000m, Currency),
                new DateOnly(2026, 6, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb,
                new QuotaAttainmentService(attDb));
            var credits2 = await svc.AllocateAsync(tx2);
            credits2.Should().HaveCount(1);
            credits2[0].CreditedAmount.Amount.Should().Be(3_500m,
                "prior=260k → all of €50k is above quota → €50k × 7% (June tx processed after April tx)");
        }
    }

    [Fact]
    public async Task AllocateAsync_SplitAtQuota_WrongDateOrder_ProducesWrongPerTxSplit()
    {
        // This test demonstrates the bug that existed WITHOUT the OrderBy fix:
        // processing the June transaction BEFORE the April transaction gives wrong
        // per-transaction credits (June gets 4% instead of 7%, April gets wrong split).
        // The fix in ProcessPendingTransactionsJobHandler (OrderBy TransactionDate) prevents this.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var q2Start = new DateOnly(2026, 4, 1);
        var q2End = new DateOnly(2026, 6, 30);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Wrong Order Plan", "desc",
                DateRange.Of(q2Start, q2End), Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Accelerator Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.AttainmentBased(EuAcceleratorTiers, splitAtQuota: true));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId, q2Start, q2End));
            db.Quotas.Add(MakeActiveQuota(tenantId, payeeId, planId, 250_000m, DateRange.Of(q2Start, q2End)));
            await db.SaveChangesAsync();
        }

        // Process tx2 (Jun 1, €50k) FIRST — wrong chronological order, simulating no OrderBy.
        // prior=0 → all €50k below quota → 4% = €2,000 (WRONG: should be €3,500 since it's above quota)
        await using (var svcDb = fixture.CreateDbForTenant(tenantId))
        await using (var attDb = fixture.CreateDbForTenant(tenantId))
        {
            var tx2 = CompensationTransaction.Ingest(
                tenantId, "WRONGORDER-TX2-JUN", payeeId, Money.Of(50_000m, Currency),
                new DateOnly(2026, 6, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());
            svcDb.CompensationTransactions.Add(tx2);

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb,
                new QuotaAttainmentService(attDb));
            var credits2 = await svc.AllocateAsync(tx2);
            credits2.Should().HaveCount(1);
            credits2[0].CreditedAmount.Amount.Should().Be(2_000m,
                "BUG: June tx processed first with prior=0 → all at 4% (should be €3,500 at 7% when prior=260k)");

            foreach (var c in credits2) svcDb.Credits.Add(c);
            tx2.MarkCalculated(1, credits2[0].CreditedAmount, "engine", Now, Guid.NewGuid());
            await svcDb.SaveChangesAsync();
        }

        // Process tx1 (Apr 1, €260k) SECOND — prior=50k (wrong, should be 0).
        // [50k, 310k] → [50k, 250k] at 4% (€200k×4%=€8k) + [250k, 310k] at 7% (€60k×7%=€4,200) = €12,200
        // WRONG: should be €10,700 (prior=0 → €250k×4% + €10k×7%)
        await using (var svcDb = fixture.CreateDbForTenant(tenantId))
        await using (var attDb = fixture.CreateDbForTenant(tenantId))
        {
            var tx1 = CompensationTransaction.Ingest(
                tenantId, "WRONGORDER-TX1-APR", payeeId, Money.Of(260_000m, Currency),
                new DateOnly(2026, 4, 1), TransactionSource.Manual, "engine",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb,
                new QuotaAttainmentService(attDb));
            var credits1 = await svc.AllocateAsync(tx1);
            credits1.Should().HaveCount(1);
            credits1[0].CreditedAmount.Amount.Should().Be(12_200m,
                "BUG: April tx processed second with prior=50k → wrong split (should be €10,700 with prior=0)");
        }
    }

    [Fact]
    public async Task AllocateAsync_SplitAtQuota_NoQuota_ReturnsZeroCommission()
    {
        // Phase 5 guard: rep with no quota → zero commission, not a silent flat-rate fallback.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Split No Quota Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Split Rule", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.AttainmentBased(EuAcceleratorTiers, splitAtQuota: true));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            // No quota seeded intentionally.
            await db.SaveChangesAsync();
        }

        await using var svcDb = fixture.CreateDbForTenant(tenantId);
        await using var attDb = fixture.CreateDbForTenant(tenantId);
        var realAttainment = new QuotaAttainmentService(attDb);

        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-NOQUOTA", payeeId, Money.Of(50_000m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb, realAttainment);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().HaveCount(1);
        credits[0].CreditedAmount.Amount.Should().Be(0m,
            "split-at-quota with no quota configured must return zero commission, not a silent fallback rate");
    }

    // ── Units measurement (WI-UNITS-MEASUREMENT) ──────────────────────────────

    private static Plan MakePlanWithUnitsFlatRule(Guid tenantId, Guid planId, decimal ratePerUnit,
        DateOnly planStart, DateOnly planEnd)
    {
        var plan = Plan.Create(tenantId, "Units Plan", "desc",
            DateRange.Of(planStart, planEnd),
            Currency, "test-user", planId, Now, Guid.NewGuid());

        plan.AddRule("Units Commission", 1,
            new Measurement { Type = MeasurementType.Units, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(ratePerUnit));

        return plan;
    }

    [Fact]
    public async Task AllocateAsync_UnitsFlat_Quantity1_EarnsRatePerUnit()
    {
        // €2.00/unit × 1 unit (default) → €2.00
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithUnitsFlatRule(tenantId, planId, ratePerUnit: 2.00m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using var svcDb = fixture.CreateDbForTenant(tenantId);
        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-UNITS-Q1", payeeId, Money.Of(500m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid(), quantity: 1);

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().HaveCount(1);
        credits[0].CreditedAmount.Amount.Should().Be(2.00m, "€2.00/unit × 1 unit");
        credits[0].CreditedAmount.Currency.Should().Be(Currency);
    }

    [Fact]
    public async Task AllocateAsync_UnitsFlat_MultipleUnits_MultipliesCorrectly()
    {
        // €2.00/unit × 10 units → €20.00 (transaction.Amount is irrelevant in Units mode)
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithUnitsFlatRule(tenantId, planId, ratePerUnit: 2.00m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using var svcDb = fixture.CreateDbForTenant(tenantId);
        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-UNITS-Q10", payeeId, Money.Of(500m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid(), quantity: 10);

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().HaveCount(1);
        credits[0].CreditedAmount.Amount.Should().Be(20.00m, "€2.00/unit × 10 units");
    }

    [Fact]
    public async Task AllocateAsync_UnitsFlat_WithPerTransactionCap_CapsResult()
    {
        // €5.00/unit × 10 units = €50.00, capped at €30.00 → €30.00
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Units Cap Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Capped Units", 1,
                new Measurement { Type = MeasurementType.Units, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(5.00m),
                cap: new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(30m, Currency) });
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using var svcDb = fixture.CreateDbForTenant(tenantId);
        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-UNITS-CAP", payeeId, Money.Of(100m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid(), quantity: 10);

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().HaveCount(1);
        credits[0].CreditedAmount.Amount.Should().Be(30.00m, "€5×10=€50 capped at €30");
    }

    [Fact]
    public async Task AllocateAsync_UnitsFlat_WithFloor_FloorApplies()
    {
        // €0.50/unit × 2 units = €1.00, floor = €5.00 → €5.00
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Units Floor Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                Currency, "test-user", planId, Now, Guid.NewGuid());
            plan.AddRule("Floored Units", 1,
                new Measurement { Type = MeasurementType.Units, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.50m),
                floor: new Floor { Amount = Money.Of(5m, Currency) });
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using var svcDb = fixture.CreateDbForTenant(tenantId);
        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-UNITS-FLOOR", payeeId, Money.Of(100m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid(), quantity: 2);

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().HaveCount(1);
        credits[0].CreditedAmount.Amount.Should().Be(5.00m, "€0.50×2=€1.00 raised to floor €5.00");
    }

    [Fact]
    public async Task AllocateAsync_Revenue_Regression_UnchangedByUnitsBranch()
    {
        // Revenue 5% × €1000 = €50 — must be identical to the behaviour before this WI.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = MakePlanWithFlatRule(tenantId, planId, rate: 0.05m,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            db.CompensationPlans.Add(plan);
            db.Payees.Add(MakePayee(tenantId, payeeId));
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payeeId,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
            await db.SaveChangesAsync();
        }

        await using var svcDb = fixture.CreateDbForTenant(tenantId);
        var tx = CompensationTransaction.Ingest(
            tenantId, "REF-REV-REG", payeeId, Money.Of(1000m, Currency),
            TxDate, TransactionSource.Manual, "user",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), svcDb);
        var credits = await svc.AllocateAsync(tx);

        credits.Should().HaveCount(1);
        credits[0].CreditedAmount.Amount.Should().Be(50.00m, "Revenue regression: 5% × €1000 = €50");
    }
}
