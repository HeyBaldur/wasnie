using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.IntegrationTests.TestDoubles;
using System.Collections.Generic;

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
        Wasnie.Infrastructure.Persistence.ApplicationDbContext db)
    {
        var clock = new FakeClock(Now.UtcDateTime);
        var guidGen = new FakeGuidGenerator();
        return new CreditAllocationService(db, guidGen, clock,
            NullLogger<CreditAllocationService>.Instance);
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

    [Fact]
    public async Task AllocateAsync_CurrencyMismatch_ThrowsDomainException()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            // Plan is in PLN, transaction in EUR.
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

            var act = async () => await svc.AllocateAsync(tx);

            await act.Should().ThrowAsync<Wasnie.Domain.Exceptions.DomainException>()
                .WithMessage("*Currency mismatch*");
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
    public async Task AllocateAsync_AttainmentBased_V1Stub_Uses100PercentBracket()
    {
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
            // V1 stub: attainment = 100% → picks the 1.00+ bracket at 12%.
            var tx = CompensationTransaction.Ingest(
                tenantId, "REF-ATTAIN", payeeId, Money.Of(1000m, Currency),
                TxDate, TransactionSource.Manual, "user",
                Guid.NewGuid(), Now, Guid.NewGuid());

            var svc = CreateService(new CreditAllocationServiceFixture.FixedTenantContext(tenantId), db);
            var credits = await svc.AllocateAsync(tx);

            credits.Should().HaveCount(1);
            credits[0].CreditedAmount.Amount.Should().Be(120m); // 1000 * 12%
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
}
