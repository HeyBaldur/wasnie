#pragma warning disable CS8602

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Common.Interfaces;
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
/// Integration tests for the A.4 Payout Engine (CalculatePayoutsForPeriodHandler).
/// Uses Testcontainers SQL Server with real migrations — owned DateOnly types require real SQL.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class PayoutEngineTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";
    private const string Pln = "PLN";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CalculatePayoutsForPeriodHandler CreateHandler(ApplicationDbContext db, Guid tenantId)
    {
        var clock = new FakeClock(Now.UtcDateTime);
        var guidGen = new FakeGuidGenerator();
        return new CalculatePayoutsForPeriodHandler(
            db, new PayoutEngineFixture.FixedTenantContext(tenantId),
            new FixedCurrentUser(), clock, guidGen,
            NullLogger<CalculatePayoutsForPeriodHandler>.Instance);
    }

    private static Payee MakePayee(Guid tenantId, string code) =>
        Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);

    private static Plan MakePlanWithFlatRule(Guid tenantId, Guid planId, string currency,
        DateOnly start, DateOnly end, decimal rate = 0.10m)
    {
        var plan = Plan.Create(tenantId, $"Plan {currency}", "desc",
            DateRange.Of(start, end), currency, "test", planId, Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(rate));
        return plan;
    }

    private static PlanAssignment MakeAssignment(Guid tenantId, Guid planId, Guid payeeId,
        string payeeName, string payeeCode, DateOnly start, DateOnly end)
    {
        var payeeRef = PayeeReference.Snapshot(payeeId, payeeName, payeeCode);
        return PlanAssignment.Create(tenantId, planId, payeeId, payeeRef,
            DateRange.Of(start, end), "test", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    private static CompensationTransaction MakeTx(Guid tenantId, Guid payeeId,
        string refNum, DateOnly date, decimal amount, string currency) =>
        CompensationTransaction.Ingest(tenantId, refNum, payeeId,
            Money.Of(amount, currency), date,
            TransactionSource.Manual, "test", Guid.NewGuid(), Now, Guid.NewGuid());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? Email => "test@test.com";
        public bool IsAuthenticated => true;
    }

    // ── Full run: creates payout with correct lines ───────────────────────────

    [Fact]
    public async Task Handle_WithCredits_CreatesPayout()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-301");
            payeeId = payee.Id;

            var plan = MakePlanWithFlatRule(tenantId, planId, Eur, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);

            var assignment = MakeAssignment(tenantId, planId, payeeId, payee.FullName, "EMP-301", start, end);
            db.PlanAssignments.Add(assignment);

            // Create transactions (already Calculated with Credits via domain logic simulation).
            await db.SaveChangesAsync();
        }

        // Now create Credits directly (simulating ProcessPendingTransactions having run).
        Guid txId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = await db.CompensationPlans.Include(p => p.Rules)
                .FirstAsync(p => p.Id == planId);

            var tx = MakeTx(tenantId, payeeId, "REF-001", new DateOnly(2026, 2, 15), 1000m, Eur);
            txId = tx.Id;
            db.CompensationTransactions.Add(tx);

            var ruleId = plan.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);
            var credit = Domain.Compensation.Credits.Credit.Allocate(
                tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
                Money.Of(1000m, Eur), Money.Of(100m, Eur),
                Percentage.FromPercent(100), CreditRole.Primary,
                "test", Guid.NewGuid(), Now, Guid.NewGuid());
            db.Credits.Add(credit);
            tx.MarkCalculated(1, Money.Of(100m, Eur), "test", Now, Guid.NewGuid());

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var cmd = new CalculatePayoutsForPeriodCommand(start, end);
            var result = await handler.Handle(cmd, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.PayoutsCreated.Should().Be(1);
            result.Value.Conflicts.Should().BeEmpty();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.PayeeId == payeeId && p.PlanId == planId);

            payout.Should().NotBeNull();
            payout!.Status.Should().Be(CompensationPayoutStatus.Calculated);
            payout.TotalCommission.Amount.Should().Be(100m);
            payout.TotalCommission.Currency.Should().Be(Eur);
            payout.Lines.Should().HaveCount(1);
            payout.Lines[0].CommissionAmount.Amount.Should().Be(100m);
        }
    }

    // ── Idempotency: re-run replaces Calculated payout, no duplicate ──────────

    [Fact]
    public async Task Handle_RerunOnCalculated_ReplacesExistingPayout()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        // Seed data + first run.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-IDEM");
            payeeId = payee.Id;
            var plan = MakePlanWithFlatRule(tenantId, planId, Eur, start, end);
            var assignment = MakeAssignment(tenantId, planId, payeeId, payee.FullName, "EMP-IDEM", start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var tx = MakeTx(tenantId, payeeId, "REF-IDEM-01", new DateOnly(2026, 2, 1), 2000m, Eur);
            db.CompensationTransactions.Add(tx);
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);
            var credit = Domain.Compensation.Credits.Credit.Allocate(
                tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
                Money.Of(2000m, Eur), Money.Of(200m, Eur),
                Percentage.FromPercent(100), CreditRole.Primary,
                "test", Guid.NewGuid(), Now, Guid.NewGuid());
            db.Credits.Add(credit);
            tx.MarkCalculated(1, Money.Of(200m, Eur), "test", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        // First run.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var result = await handler.Handle(
                new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            result.Value!.PayoutsCreated.Should().Be(1);
        }

        // Second run — should replace the existing Calculated payout.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var result = await handler.Handle(
                new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            result.Value!.PayoutsCreated.Should().Be(1);
        }

        // Only ONE payout should exist — no duplicates.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payouts = await db.CompensationPayouts
                .Where(p => p.PayeeId == payeeId && p.PlanId == planId)
                .ToListAsync();

            payouts.Should().HaveCount(1, "re-run must replace the Calculated payout, not duplicate it");
            payouts[0].Status.Should().Be(CompensationPayoutStatus.Calculated);
        }
    }

    // ── Approved payout blocks recalculation ──────────────────────────────────

    [Fact]
    public async Task Handle_ApprovedPayout_ReportsConflictAndSkips()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        // Seed + first run + approve.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-APR");
            payeeId = payee.Id;
            var plan = MakePlanWithFlatRule(tenantId, planId, Eur, start, end);
            var assignment = MakeAssignment(tenantId, planId, payeeId, payee.FullName, "EMP-APR", start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        // First run creates a Calculated payout (no credits — empty payout is fine for this test).
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            await handler.Handle(new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);
        }

        // Approve the payout.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts
                .FirstAsync(p => p.PayeeId == payeeId && p.PlanId == planId);
            payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        // Second run must report conflict and NOT replace the Approved payout.
        CalculatePayoutsResult result;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var r = await handler.Handle(
                new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);
            r.IsSuccess.Should().BeTrue();
            result = r.Value!;
        }

        result.PayoutsCreated.Should().Be(0);
        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].Status.Should().Be("Approved");
        result.Conflicts[0].PayeeId.Should().Be(payeeId);

        // Approved payout must still be there.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts
                .FirstAsync(p => p.PayeeId == payeeId && p.PlanId == planId);
            payout.Status.Should().Be(CompensationPayoutStatus.Approved);
        }
    }

    // ── Pattern B multi-currency: two plans → two payouts ────────────────────

    [Fact]
    public async Task Handle_PatternB_TwoCurrencies_CreatesTwoSeparatePayouts()
    {
        var tenantId = Guid.NewGuid();
        var planEurId = Guid.NewGuid();
        var planPlnId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 4, 1);
        var end = new DateOnly(2026, 6, 30);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-MULTI");
            payeeId = payee.Id;
            db.Payees.Add(payee);

            var planEur = MakePlanWithFlatRule(tenantId, planEurId, Eur, start, end);
            var planPln = MakePlanWithFlatRule(tenantId, planPlnId, Pln, start, end);
            db.CompensationPlans.Add(planEur);
            db.CompensationPlans.Add(planPln);

            // One assignment per plan (Pattern B: currency-based routing).
            var assignmentEur = MakeAssignment(tenantId, planEurId, payeeId, payee.FullName, "EMP-MULTI", start, end);
            var assignmentPln = MakeAssignment(tenantId, planPlnId, payeeId, payee.FullName, "EMP-MULTI", start, end);
            db.PlanAssignments.Add(assignmentEur);
            db.PlanAssignments.Add(assignmentPln);
            await db.SaveChangesAsync();
        }

        // Add one EUR Credit and one PLN Credit.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var planEur = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planEurId);
            var planPln = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planPlnId);

            var txDate = new DateOnly(2026, 5, 15);
            void AddCreditForPlan(Plan plan, string currency, string refNum)
            {
                var ruleId = plan.Rules.First().Id;
                var snapshot = RuleSnapshot.Freeze(ruleId, plan.Id, 1, "Commission",
                    RateTable.Flat(0.10m), Trigger.Always(), Now);
                var tx = MakeTx(tenantId, payeeId, refNum, txDate, 500m, currency);
                db.CompensationTransactions.Add(tx);
                var credit = Domain.Compensation.Credits.Credit.Allocate(
                    tenantId, tx.Id, payeeId, plan.Id, ruleId, snapshot,
                    Money.Of(500m, currency), Money.Of(50m, currency),
                    Percentage.FromPercent(100), CreditRole.Primary,
                    "test", Guid.NewGuid(), Now, Guid.NewGuid());
                db.Credits.Add(credit);
                    tx.MarkCalculated(1, Money.Of(50m, currency), "test", Now, Guid.NewGuid());
            }

            AddCreditForPlan(planEur, Eur, "REF-EUR-01");
            AddCreditForPlan(planPln, Pln, "REF-PLN-01");
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var result = await handler.Handle(
                new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.PayoutsCreated.Should().Be(2);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payouts = await db.CompensationPayouts
                .Include(p => p.Lines)
                .Where(p => p.PayeeId == payeeId)
                .ToListAsync();

            payouts.Should().HaveCount(2, "one payout per plan/currency");
            payouts.Should().Contain(p => p.TotalCommission.Currency == Eur && p.TotalCommission.Amount == 50m);
            payouts.Should().Contain(p => p.TotalCommission.Currency == Pln && p.TotalCommission.Amount == 50m);
        }
    }

    // ── CARTESIAN BUG GUARD: multi-rule plan must NOT 20x-inflate amounts ─────
    // This test explicitly catches the Cartesian product bug that appeared in A.3-FIX-2
    // and V3-FIX-2. If the Credit aggregation uses an unsafe join it will inflate.

    [Fact]
    public async Task Handle_MultiRulePlan_TotalCommissionIsNotCartesianInflated()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-CART");
            payeeId = payee.Id;
            db.Payees.Add(payee);

            // Plan with TWO rules — each transaction generates TWO Credits.
            var plan = Plan.Create(tenantId, "Multi-Rule Plan", "desc",
                DateRange.Of(start, end), Eur, "test", planId, Now, Guid.NewGuid());
            plan.AddRule("Rule A", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.05m));
            plan.AddRule("Rule B", 2,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.03m));
            db.CompensationPlans.Add(plan);

            var assignment = MakeAssignment(tenantId, planId, payeeId, payee.FullName, "EMP-CART", start, end);
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        // 5 transactions × 2 rules = 10 Credits.
        // Expected total = 5 × 1000 × (0.05 + 0.03) = 5 × 80 = 400 EUR.
        // Cartesian bug would 20x this to 8000 EUR if Credits were joined with Transactions incorrectly.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var rules = plan.Rules.ToList();

            for (var i = 1; i <= 5; i++)
            {
                var txDate = new DateOnly(2026, 2, i);
                var tx = MakeTx(tenantId, payeeId, $"REF-CART-{i:D2}", txDate, 1000m, Eur);
                db.CompensationTransactions.Add(tx);

                foreach (var rule in rules)
                {
                    var rate = rule.SortOrder == 1 ? 0.05m : 0.03m;
                    var snapshot = RuleSnapshot.Freeze(rule.Id, planId, 1, rule.Name,
                        RateTable.Flat(rate), Trigger.Always(), Now);
                    var credit = Domain.Compensation.Credits.Credit.Allocate(
                        tenantId, tx.Id, payeeId, planId, rule.Id, snapshot,
                        Money.Of(1000m, Eur), Money.Of(1000m * rate, Eur),
                        Percentage.FromPercent(100), CreditRole.Primary,
                        "test", Guid.NewGuid(), Now, Guid.NewGuid());
                    db.Credits.Add(credit);
                }

                    tx.MarkCalculated(2, Money.Of(80m, Eur), "test", Now, Guid.NewGuid());
            }

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var result = await handler.Handle(
                new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.PayoutsCreated.Should().Be(1);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts
                .Include(p => p.Lines)
                .FirstAsync(p => p.PayeeId == payeeId && p.PlanId == planId);

            // 10 PayoutLines (5 tx × 2 rules).
            payout.Lines.Should().HaveCount(10,
                "one PayoutLine per Credit — 5 transactions × 2 rules");

            // Total must be 400 EUR, NOT 8000 EUR (the Cartesian 20× inflation).
            payout.TotalCommission.Amount.Should().Be(400m,
                "5 × 1000 × (0.05 + 0.03) = 400 — Cartesian bug would produce 8000");
        }
    }

    // ── Warning: pending transactions reported correctly ──────────────────────

    [Fact]
    public async Task Handle_PendingTransactions_ReportsWarning()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-WARN");
            payeeId = payee.Id;
            var plan = MakePlanWithFlatRule(tenantId, planId, Eur, start, end);
            var assignment = MakeAssignment(tenantId, planId, payeeId, payee.FullName, "EMP-WARN", start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(assignment);

            // Two Pending transactions in the period — no Credits created yet.
            var tx1 = MakeTx(tenantId, payeeId, "REF-WARN-01", new DateOnly(2026, 2, 1), 500m, Eur);
            var tx2 = MakeTx(tenantId, payeeId, "REF-WARN-02", new DateOnly(2026, 2, 15), 500m, Eur);
            db.CompensationTransactions.Add(tx1);
            db.CompensationTransactions.Add(tx2);
            await db.SaveChangesAsync();
        }

        CalculatePayoutsResult result;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var r = await handler.Handle(
                new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);
            r.IsSuccess.Should().BeTrue();
            result = r.Value!;
        }

        result.Warnings.Should().HaveCount(1);
        result.Warnings[0].PendingTransactionCount.Should().Be(2);
        result.Warnings[0].PayeeId.Should().Be(payeeId);
        // Payout is still created (with zero lines — empty payout).
        result.PayoutsCreated.Should().Be(1);
    }

    // ── Intersection period: payout covers overlap, not full command period ───

    [Fact]
    public async Task Handle_AssignmentStartsMidPeriod_PayoutCoversIntersectionOnly()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var cmdStart = new DateOnly(2026, 4, 1);
        var cmdEnd = new DateOnly(2026, 6, 30);
        // Assignment only starts on May 1st — so intersection is May 1 – June 30.
        var assignStart = new DateOnly(2026, 5, 1);
        var assignEnd = new DateOnly(2026, 12, 31);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-INTER");
            payeeId = payee.Id;
            var plan = MakePlanWithFlatRule(tenantId, planId, Eur, assignStart, assignEnd);
            var assignment = MakeAssignment(tenantId, planId, payeeId, payee.FullName, "EMP-INTER", assignStart, assignEnd);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            var result = await handler.Handle(
                new CalculatePayoutsForPeriodCommand(cmdStart, cmdEnd), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.PayoutsCreated.Should().Be(1);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts
                .FirstAsync(p => p.PayeeId == payeeId && p.PlanId == planId);

            // Period must be the intersection, not the command period.
            payout.Period.Start.Should().Be(assignStart,
                "payout period starts when the assignment started, not when the command period starts");
            payout.Period.End.Should().Be(cmdEnd);
        }
    }

    // ── Only non-superseded Credits are included ──────────────────────────────

    [Fact]
    public async Task Handle_SupersededCredits_AreExcludedFromPayout()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-SUP");
            payeeId = payee.Id;
            var plan = MakePlanWithFlatRule(tenantId, planId, Eur, start, end);
            var assignment = MakeAssignment(tenantId, planId, payeeId, payee.FullName, "EMP-SUP", start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(assignment);
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var txDate = new DateOnly(2026, 2, 1);

            // One valid Credit + one superseded Credit for same transaction.
            var tx = MakeTx(tenantId, payeeId, "REF-SUP-01", txDate, 1000m, Eur);
            db.CompensationTransactions.Add(tx);

            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var activeCredit = Domain.Compensation.Credits.Credit.Allocate(
                tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
                Money.Of(1000m, Eur), Money.Of(100m, Eur),
                Percentage.FromPercent(100), CreditRole.Primary,
                "test", Guid.NewGuid(), Now, Guid.NewGuid());
            db.Credits.Add(activeCredit);

            var supersededCredit = Domain.Compensation.Credits.Credit.Allocate(
                tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
                Money.Of(1000m, Eur), Money.Of(999m, Eur),
                Percentage.FromPercent(100), CreditRole.Primary,
                "test", Guid.NewGuid(), Now, Guid.NewGuid());
            supersededCredit.Supersede("reassignment", Now, Guid.NewGuid());
            db.Credits.Add(supersededCredit);

            tx.MarkCalculated(1, Money.Of(100m, Eur), "test", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var handler = CreateHandler(db, tenantId);
            await handler.Handle(new CalculatePayoutsForPeriodCommand(start, end), CancellationToken.None);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts
                .Include(p => p.Lines)
                .FirstAsync(p => p.PayeeId == payeeId && p.PlanId == planId);

            // Only the non-superseded Credit should be included.
            payout.Lines.Should().HaveCount(1, "superseded Credits must be excluded");
            payout.TotalCommission.Amount.Should().Be(100m, "only the active Credit's amount");
        }
    }
}

