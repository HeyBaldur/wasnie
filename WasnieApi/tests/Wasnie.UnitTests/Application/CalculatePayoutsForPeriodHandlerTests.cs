using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// Unit net (EF InMemory, no Docker) around CalculatePayoutsForPeriodHandler — the payout
/// AGGREGATION, as opposed to CommissionCalculatorTests which covers the per-transaction math.
///
/// These tests pin the CURRENT behaviour (anti-regression) of the four money-critical zones:
///   • anti-double-pay credit filter   — handler :168-169  (SupersededAt == null && ConsumedAt == null)
///   • payee × plan × period grouping  — handler :100-110, :163-171
///   • period intersection             — handler :80-85
///   • Approved/Paid blocking          — handler :112-127
///
/// The Paid-clawback work will net a negative adjustment exactly at :168-169, so anything that
/// changes there must break a test here first.
///
/// Tests marked "PINS CURRENT BEHAVIOUR — review if desired" fix behaviour that is arguably
/// debatable; they are reported, not "fixed" here.
/// </summary>
public sealed class CalculatePayoutsForPeriodHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private sealed record Harness(
        ApplicationDbContext Db,
        CalculatePayoutsForPeriodHandler Handler,
        Guid TenantId);

    private static Harness Build(string dbName) => Attach(dbName, Guid.NewGuid());

    /// <summary>
    /// A fresh DbContext + handler over an existing in-memory store — the unit-test equivalent of a
    /// second HTTP request (new scope, nothing left in the change tracker).
    /// </summary>
    private static Harness Attach(string dbName, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin");

        var handler = new CalculatePayoutsForPeriodHandler(
            db, tenantCtx, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            Substitute.For<ILogger<CalculatePayoutsForPeriodHandler>>());

        return new Harness(db, handler, tenantId);
    }

    // ── Seeding helpers ─────────────────────────────────────────────────────────

    private static Plan SeedPlan(Harness h, string currency = Eur, bool archived = false)
    {
        var plan = Plan.Create(
            h.TenantId, $"Plan {currency}", "desc",
            DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            currency, "admin", Guid.NewGuid(), Now, Guid.NewGuid());
        if (archived)
        {
            // Archive is only reachable from Active, and Active requires at least one rule.
            plan.AddRule(
                "Base Commission", sortOrder: 1,
                measurement: new Measurement
                {
                    Type = MeasurementType.Revenue,
                    SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum,
                },
                rateTable: RateTable.Flat(0.05m));
            plan.Activate("admin", Now, Guid.NewGuid());
            plan.Archive("admin", Now, Guid.NewGuid());
        }
        h.Db.CompensationPlans.Add(plan);
        h.Db.SaveChanges();
        return plan;
    }

    private static Guid SeedAssignment(
        Harness h, Guid payeeId, Guid planId, DateOnly start, DateOnly end, string payeeName = "Payee")
    {
        var assignment = PlanAssignment.Create(
            h.TenantId, planId, payeeId,
            PayeeReference.Snapshot(payeeId, payeeName, "E1"),
            DateRange.Of(start, end), "admin", Guid.NewGuid(), Now, Guid.NewGuid());
        h.Db.PlanAssignments.Add(assignment);
        h.Db.SaveChanges();
        return assignment.Id;
    }

    /// <summary>Calculated transaction (already processed) — the normal input to a payout.</summary>
    private static CompensationTransaction SeedTransaction(
        Harness h, Guid payeeId, DateOnly date, decimal amount, string currency = Eur,
        bool pending = false)
    {
        var tx = CompensationTransaction.Ingest(
            h.TenantId, $"TX-{Guid.NewGuid():N}"[..12], payeeId, Money.Of(amount, currency),
            date, TransactionSource.Manual, "admin", Guid.NewGuid(), Now, Guid.NewGuid());
        if (!pending)
            tx.MarkCalculated(1, Money.Of(0m, currency), "admin", Now, Guid.NewGuid());
        h.Db.CompensationTransactions.Add(tx);
        h.Db.SaveChanges();
        return tx;
    }

    private static Credit SeedCredit(
        Harness h, Guid txId, Guid payeeId, Guid planId, decimal commission,
        string currency = Eur, decimal baseAmount = 1000m,
        bool superseded = false, bool consumed = false)
    {
        var ruleId = Guid.NewGuid();
        var snapshot = RuleSnapshot.Freeze(
            ruleId, planId, 1, "Commission", RateTable.Flat(0.10m), Trigger.Always(), Now);

        var credit = Credit.Allocate(
            h.TenantId, txId, payeeId, planId, ruleId, snapshot,
            Money.Of(baseAmount, currency), Money.Of(commission, currency),
            Percentage.FromPercent(100), CreditRole.Primary,
            "admin", Guid.NewGuid(), Now, Guid.NewGuid());

        if (superseded) credit.Supersede("test", Now, Guid.NewGuid());
        if (consumed) credit.Consume(Guid.NewGuid(), Now, Guid.NewGuid());

        h.Db.Credits.Add(credit);
        h.Db.SaveChanges();
        return credit;
    }

    /// <summary>payee + plan + assignment + one Calculated tx, all covering June 2026.</summary>
    private static (Guid PayeeId, Plan Plan, CompensationTransaction Tx) SeedJuneScenario(Harness h)
    {
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 15), 1000m);
        return (payeeId, plan, tx);
    }

    /// <summary>A real Payee row (the other tests only need an id). Terminated when asked.</summary>
    private static Guid SeedPayee(Harness h, bool terminated, string code = "E1")
    {
        var payee = Payee.Create(
            h.TenantId, $"Payee {code}", code, $"{code}-{Guid.NewGuid():N}@test.com",
            new DateOnly(2020, 1, 1), "admin", Guid.NewGuid(), Now);
        if (terminated)
            payee.MarkAsTerminated(new DateOnly(2026, 5, 31), "admin", Now);
        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
        return payee.Id;
    }

    private static CalculatePayoutsForPeriodCommand June(Guid? payeeFilter = null) =>
        new(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), payeeFilter);

    // ══ 1. Anti-double-pay credit filter (handler :168-169) ═════════════════════
    // THE test of this file: the line the Paid-clawback will extend.

    [Fact]
    public async Task Live_credit_both_timestamps_null_is_paid()
    {
        var h = Build(nameof(Live_credit_both_timestamps_null_is_paid));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);

        var result = await h.Handler.Handle(June(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PayoutsCreated.Should().Be(1);
        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(100m);
        payout.Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task Superseded_credit_is_excluded_from_the_payout()
    {
        var h = Build(nameof(Superseded_credit_is_excluded_from_the_payout));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m, superseded: true);

        var result = await h.Handler.Handle(June(), default);

        result.IsSuccess.Should().BeTrue();
        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.Lines.Should().BeEmpty();
        payout.TotalCommission.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task Consumed_credit_is_excluded_from_the_payout()
    {
        var h = Build(nameof(Consumed_credit_is_excluded_from_the_payout));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m, consumed: true);

        var result = await h.Handler.Handle(June(), default);

        result.IsSuccess.Should().BeTrue();
        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.Lines.Should().BeEmpty();
        payout.TotalCommission.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task Only_the_live_credit_of_a_live_superseded_consumed_trio_is_paid()
    {
        var h = Build(nameof(Only_the_live_credit_of_a_live_superseded_consumed_trio_is_paid));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 250m, superseded: true);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 500m, consumed: true);

        await h.Handler.Handle(June(), default);

        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(100m);
        payout.Lines.Should().HaveCount(1);
    }

    // ══ 2. Grouping payee × plan × period ═══════════════════════════════════════

    [Fact]
    public async Task Two_payees_get_two_separate_payouts_and_amounts_do_not_mix()
    {
        var h = Build(nameof(Two_payees_get_two_separate_payouts_and_amounts_do_not_mix));
        var plan = SeedPlan(h);
        var payeeA = Guid.NewGuid();
        var payeeB = Guid.NewGuid();
        SeedAssignment(h, payeeA, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "A");
        SeedAssignment(h, payeeB, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "B");
        var txA = SeedTransaction(h, payeeA, new DateOnly(2026, 6, 10), 1000m);
        var txB = SeedTransaction(h, payeeB, new DateOnly(2026, 6, 11), 2000m);
        SeedCredit(h, txA.Id, payeeA, plan.Id, 100m);
        SeedCredit(h, txB.Id, payeeB, plan.Id, 200m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(2);
        var payouts = await h.Db.CompensationPayouts.ToListAsync();
        payouts.Single(p => p.PayeeId == payeeA).TotalCommission.Amount.Should().Be(100m);
        payouts.Single(p => p.PayeeId == payeeB).TotalCommission.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task Same_payee_on_two_plans_gets_one_payout_per_plan_with_only_its_own_credits()
    {
        var h = Build(nameof(Same_payee_on_two_plans_gets_one_payout_per_plan_with_only_its_own_credits));
        var payeeId = Guid.NewGuid();
        var planA = SeedPlan(h);
        var planB = SeedPlan(h);
        SeedAssignment(h, payeeId, planA.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        SeedAssignment(h, payeeId, planB.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 10), 1000m);
        SeedCredit(h, tx.Id, payeeId, planA.Id, 100m);
        SeedCredit(h, tx.Id, payeeId, planB.Id, 55m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(2);
        var payouts = await h.Db.CompensationPayouts.ToListAsync();
        payouts.Single(p => p.PlanId == planA.Id).TotalCommission.Amount.Should().Be(100m);
        payouts.Single(p => p.PlanId == planA.Id).Lines.Should().HaveCount(1);
        payouts.Single(p => p.PlanId == planB.Id).TotalCommission.Amount.Should().Be(55m);
    }

    [Fact]
    public async Task Multiple_credits_sum_exactly_to_the_cent()
    {
        var h = Build(nameof(Multiple_credits_sum_exactly_to_the_cent));
        var s = SeedJuneScenario(h);
        var tx2 = SeedTransaction(h, s.PayeeId, new DateOnly(2026, 6, 20), 500m);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 33.33m);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 33.33m);
        SeedCredit(h, tx2.Id, s.PayeeId, s.Plan.Id, 33.34m);

        await h.Handler.Handle(June(), default);

        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(100.00m);
        payout.Lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task Credits_of_another_payee_never_enter_this_payees_payout()
    {
        var h = Build(nameof(Credits_of_another_payee_never_enter_this_payees_payout));
        var s = SeedJuneScenario(h);
        var otherPayee = Guid.NewGuid();
        var otherTx = SeedTransaction(h, otherPayee, new DateOnly(2026, 6, 10), 9000m);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);
        // Other payee has a credit on the same plan but NO assignment → no payout of its own.
        SeedCredit(h, otherTx.Id, otherPayee, s.Plan.Id, 900m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(1);
        (await h.Db.CompensationPayouts.SingleAsync()).TotalCommission.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task PayeeIdFilter_restricts_the_run_to_that_payee()
    {
        var h = Build(nameof(PayeeIdFilter_restricts_the_run_to_that_payee));
        var plan = SeedPlan(h);
        var payeeA = Guid.NewGuid();
        var payeeB = Guid.NewGuid();
        SeedAssignment(h, payeeA, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "A");
        SeedAssignment(h, payeeB, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "B");
        var txA = SeedTransaction(h, payeeA, new DateOnly(2026, 6, 10), 1000m);
        SeedCredit(h, txA.Id, payeeA, plan.Id, 100m);

        var result = await h.Handler.Handle(June(payeeFilter: payeeA), default);

        result.Value!.PayoutsCreated.Should().Be(1);
        (await h.Db.CompensationPayouts.SingleAsync()).PayeeId.Should().Be(payeeA);
    }

    // ══ 3. Period intersection (handler :80-85) ═════════════════════════════════

    [Fact]
    public async Task Transaction_inside_the_period_is_included_and_one_outside_is_not()
    {
        var h = Build(nameof(Transaction_inside_the_period_is_included_and_one_outside_is_not));
        var s = SeedJuneScenario(h);
        var julyTx = SeedTransaction(h, s.PayeeId, new DateOnly(2026, 7, 5), 1000m);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);
        SeedCredit(h, julyTx.Id, s.PayeeId, s.Plan.Id, 999m);

        await h.Handler.Handle(June(), default);

        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Both_period_boundaries_are_inclusive()
    {
        var h = Build(nameof(Both_period_boundaries_are_inclusive));
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var firstDay = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 1), 1000m);
        var lastDay = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 30), 1000m);
        var dayBefore = SeedTransaction(h, payeeId, new DateOnly(2026, 5, 31), 1000m);
        SeedCredit(h, firstDay.Id, payeeId, plan.Id, 10m);
        SeedCredit(h, lastDay.Id, payeeId, plan.Id, 20m);
        SeedCredit(h, dayBefore.Id, payeeId, plan.Id, 40m);

        await h.Handler.Handle(June(), default);

        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(30m);
        payout.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Payout_period_is_the_intersection_when_the_assignment_is_narrower()
    {
        var h = Build(nameof(Payout_period_is_the_intersection_when_the_assignment_is_narrower));
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h);
        // Assignment covers only 10–20 June; the run asks for the whole month.
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 20));
        var inside = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 15), 1000m);
        var outside = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 25), 1000m);
        SeedCredit(h, inside.Id, payeeId, plan.Id, 100m);
        SeedCredit(h, outside.Id, payeeId, plan.Id, 700m);

        await h.Handler.Handle(June(), default);

        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.Period.Start.Should().Be(new DateOnly(2026, 6, 10));
        payout.Period.End.Should().Be(new DateOnly(2026, 6, 20));
        payout.TotalCommission.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Assignment_that_does_not_overlap_the_period_produces_no_payout()
    {
        var h = Build(nameof(Assignment_that_does_not_overlap_the_period_produces_no_payout));
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 15), 1000m);
        SeedCredit(h, tx.Id, payeeId, plan.Id, 100m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(0);
        (await h.Db.CompensationPayouts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Inverted_period_is_rejected()
    {
        var h = Build(nameof(Inverted_period_is_rejected));

        var result = await h.Handler.Handle(
            new CalculatePayoutsForPeriodCommand(new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 1)),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("PeriodStart must be on or before PeriodEnd");
    }

    // ══ 4. Approved / Paid blocking (handler :112-127) ══════════════════════════

    [Theory]
    [InlineData(CompensationPayoutStatus.Approved)]
    [InlineData(CompensationPayoutStatus.Paid)]
    public async Task Existing_approved_or_paid_payout_for_the_same_period_blocks_and_reports_a_conflict(
        CompensationPayoutStatus status)
    {
        var h = Build($"{nameof(Existing_approved_or_paid_payout_for_the_same_period_blocks_and_reports_a_conflict)}_{status}");
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);
        SeedExistingPayout(h, s.PayeeId, s.Plan.Id,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), status, amount: 42m);

        var result = await h.Handler.Handle(June(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PayoutsCreated.Should().Be(0);
        result.Value.Conflicts.Should().ContainSingle()
            .Which.Status.Should().Be(status.ToString());
        // The pre-existing payout is untouched — no second payout for the same period.
        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(42m);
    }

    [Fact]
    public async Task Stale_calculated_payout_is_replaced_on_re_run()
    {
        var h = Build(nameof(Stale_calculated_payout_is_replaced_on_re_run));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);
        SeedExistingPayout(h, s.PayeeId, s.Plan.Id,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            CompensationPayoutStatus.Calculated, amount: 42m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(1);
        result.Value.Conflicts.Should().BeEmpty();
        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Running_the_handler_twice_is_idempotent_and_leaves_one_payout()
    {
        var h = Build(nameof(Running_the_handler_twice_is_idempotent_and_leaves_one_payout));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);

        await h.Handler.Handle(June(), default);
        await h.Handler.Handle(June(), default);

        var payouts = await h.Db.CompensationPayouts.ToListAsync();
        payouts.Should().ContainSingle();
        payouts[0].TotalCommission.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Approved_payout_for_a_different_period_does_not_block()
    {
        var h = Build(nameof(Approved_payout_for_a_different_period_does_not_block));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);
        // Approved payout for May — a different period, so it must not interfere with the June run.
        SeedExistingPayout(h, s.PayeeId, s.Plan.Id,
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31),
            CompensationPayoutStatus.Approved, amount: 42m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(1);
        result.Value.Conflicts.Should().BeEmpty();
    }

    /// <summary>
    /// PINS CURRENT BEHAVIOUR — review if desired (candidate finding for the clawback WI).
    ///
    /// The Approved/Paid block matches on the EXACT intersection period. An Approved payout for
    /// 1–30 June therefore does NOT block a re-run scoped to 1–15 June: the narrower run creates a
    /// second payout containing the very same credit. Approved credits are not Consumed yet
    /// (ConsumedAt is only stamped when a payout is marked Paid), so the :168-169 guard does not
    /// catch it either. Net effect today: the same credit can sit in an Approved payout AND in a
    /// new Calculated one at the same time.
    /// </summary>
    [Fact]
    public async Task Approved_payout_does_not_block_a_narrower_re_run_and_the_credit_is_counted_twice()
    {
        const string dbName = nameof(Approved_payout_does_not_block_a_narrower_re_run_and_the_credit_is_counted_twice);
        var h = Build(dbName);
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 10), 1000m);
        var credit = SeedCredit(h, tx.Id, payeeId, plan.Id, 100m);

        // Run 1: whole month → payout 1–30 June, then approved.
        await h.Handler.Handle(June(), default);
        var first = await h.Db.CompensationPayouts.SingleAsync();
        first.Approve("admin", Now, Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        // Run 2 goes through a fresh context (a second request): first half of the month only →
        // different intersection → not blocked.
        var h2 = Attach(dbName, h.TenantId);
        var result = await h2.Handler.Handle(
            new CalculatePayoutsForPeriodCommand(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15)),
            default);

        result.Value!.PayoutsCreated.Should().Be(1);
        result.Value.Conflicts.Should().BeEmpty();

        var payouts = await h2.Db.CompensationPayouts.Include(p => p.Lines).ToListAsync();
        payouts.Should().HaveCount(2);
        payouts.Sum(p => p.TotalCommission.Amount).Should().Be(200m);
        payouts.Should().OnlyContain(p => p.Lines.Any(l => l.CreditId == credit.Id));
    }

    // ══ 5. Edge cases ══════════════════════════════════════════════════════════

    /// <summary>
    /// PINS CURRENT BEHAVIOUR — review if desired: with no payable credit the handler still
    /// creates a zero payout and counts it in PayoutsCreated, rather than skipping the payee.
    /// </summary>
    [Fact]
    public async Task No_credits_still_creates_a_zero_payout_in_the_plan_currency()
    {
        var h = Build(nameof(No_credits_still_creates_a_zero_payout_in_the_plan_currency));
        SeedJuneScenario(h);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(1);
        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.TotalCommission.Amount.Should().Be(0m);
        payout.TotalCommission.Currency.Should().Be(Eur);
        payout.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Transaction_in_another_currency_than_the_plan_is_ignored()
    {
        var h = Build(nameof(Transaction_in_another_currency_than_the_plan_is_ignored));
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h, Eur);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var usdTx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 10), 1000m, "USD");
        SeedCredit(h, usdTx.Id, payeeId, plan.Id, 100m, "USD");

        await h.Handler.Handle(June(), default);

        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.Lines.Should().BeEmpty();
        payout.TotalCommission.Should().Be(Money.Of(0m, Eur));
    }

    [Fact]
    public async Task Two_plans_in_two_currencies_produce_one_payout_per_currency()
    {
        var h = Build(nameof(Two_plans_in_two_currencies_produce_one_payout_per_currency));
        var payeeId = Guid.NewGuid();
        var eurPlan = SeedPlan(h, Eur);
        var usdPlan = SeedPlan(h, "USD");
        SeedAssignment(h, payeeId, eurPlan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        SeedAssignment(h, payeeId, usdPlan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var eurTx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 10), 1000m, Eur);
        var usdTx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 10), 1000m, "USD");
        SeedCredit(h, eurTx.Id, payeeId, eurPlan.Id, 100m, Eur);
        SeedCredit(h, usdTx.Id, payeeId, usdPlan.Id, 200m, "USD");

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(2);
        var payouts = await h.Db.CompensationPayouts.ToListAsync();
        payouts.Single(p => p.PlanId == eurPlan.Id).TotalCommission.Should().Be(Money.Of(100m, Eur));
        payouts.Single(p => p.PlanId == usdPlan.Id).TotalCommission.Should().Be(Money.Of(200m, "USD"));
    }

    [Fact]
    public async Task Archived_plan_never_contributes_a_payout()
    {
        var h = Build(nameof(Archived_plan_never_contributes_a_payout));
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h, Eur, archived: true);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 10), 1000m);
        SeedCredit(h, tx.Id, payeeId, plan.Id, 100m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(0);
        (await h.Db.CompensationPayouts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Pending_transactions_raise_a_warning_with_their_count()
    {
        var h = Build(nameof(Pending_transactions_raise_a_warning_with_their_count));
        var s = SeedJuneScenario(h);
        SeedCredit(h, s.Tx.Id, s.PayeeId, s.Plan.Id, 100m);
        SeedTransaction(h, s.PayeeId, new DateOnly(2026, 6, 20), 500m, pending: true);
        SeedTransaction(h, s.PayeeId, new DateOnly(2026, 6, 21), 500m, pending: true);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(1);
        var warning = result.Value.Warnings.Should().ContainSingle().Subject;
        warning.PendingTransactionCount.Should().Be(2);
        warning.PayeeId.Should().Be(s.PayeeId);
        // The payout is still created with only the credited part.
        (await h.Db.CompensationPayouts.SingleAsync()).TotalCommission.Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Deactivated_assignment_produces_no_payout()
    {
        var h = Build(nameof(Deactivated_assignment_produces_no_payout));
        var payeeId = Guid.NewGuid();
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var assignment = await h.Db.PlanAssignments.SingleAsync();
        assignment.Deactivate("admin", Now, Guid.NewGuid());
        await h.Db.SaveChangesAsync();
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 10), 1000m);
        SeedCredit(h, tx.Id, payeeId, plan.Id, 100m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(0);
        (await h.Db.CompensationPayouts.CountAsync()).Should().Be(0);
    }

    // ── helper: pre-existing payout in a given status ───────────────────────────

    private static void SeedExistingPayout(
        Harness h, Guid payeeId, Guid planId, DateOnly start, DateOnly end,
        CompensationPayoutStatus status, decimal amount)
    {
        var spec = new PayoutLineSpec(
            Guid.NewGuid(), Guid.NewGuid(), "Legacy",
            Money.Of(1000m, Eur), Money.Of(amount, Eur), []);

        var payout = CompensationPayout.Calculate(
            h.TenantId, payeeId, planId, PayeeReference.Snapshot(payeeId, "Payee", "E1"),
            DateRange.Of(start, end), [spec], Eur, "admin",
            Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);

        if (status is CompensationPayoutStatus.Approved or CompensationPayoutStatus.Paid)
            payout.Approve("admin", Now, Guid.NewGuid());
        if (status == CompensationPayoutStatus.Paid)
            payout.MarkPaid("admin", Now);

        h.Db.CompensationPayouts.Add(payout);
        h.Db.SaveChanges();
    }

    // ══ 5. The circuit breaker: payees who have left ════════════════════════════
    // A terminated payee earns nothing further. Generating payouts for them creates a ghost the engine
    // re-processes every run, and makes an outstanding clawback look collectable against commissions
    // that will never exist. The freeze is an EXCLUSION here — nothing is written to the ledger.

    [Fact]
    public async Task A_terminated_payee_is_excluded_from_the_pay_run()
    {
        var h = Build(nameof(A_terminated_payee_is_excluded_from_the_pay_run));
        var payeeId = SeedPayee(h, terminated: true);
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 15), 1000m);
        SeedCredit(h, tx.Id, payeeId, plan.Id, 100m);

        var result = await h.Handler.Handle(June(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PayoutsCreated.Should().Be(0, "someone who has left gets no new payout");
        (await h.Db.CompensationPayouts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Excluding_a_terminated_payee_writes_nothing_to_their_ledger()
    {
        // The freeze must be invisible to the ledger: no flag, no entry, no erasure. The debt stays
        // exactly as it was, waiting for a person to close it.
        var h = Build(nameof(Excluding_a_terminated_payee_writes_nothing_to_their_ledger));
        var payeeId = SeedPayee(h, terminated: true);
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var debt = PayeeLedgerEntry.CreateSystemEntry(
            h.TenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(500m, Eur),
            "Churned deal.", LedgerSourceType.DealChurn, "system", Guid.NewGuid(), Now, Guid.NewGuid());
        var balance = PayeeBalance.Open(h.TenantId, payeeId, Eur, Guid.NewGuid(), Now);
        balance.Apply(debt, Now);
        h.Db.PayeeLedgerEntries.Add(debt);
        h.Db.PayeeBalances.Add(balance);
        await h.Db.SaveChangesAsync();

        await h.Handler.Handle(June(), default);

        (await h.Db.PayeeLedgerEntries.CountAsync(e => e.PayeeId == payeeId)).Should().Be(1);
        (await h.Db.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId))
            .Balance.Amount.Should().Be(-500m, "the debt is frozen, not forgiven and not moved");
    }

    [Fact]
    public async Task An_active_payee_with_debt_still_enters_the_pay_run()
    {
        // The control that keeps the live case working: debt is netted from people who still earn.
        var h = Build(nameof(An_active_payee_with_debt_still_enters_the_pay_run));
        var payeeId = SeedPayee(h, terminated: false);
        var plan = SeedPlan(h);
        SeedAssignment(h, payeeId, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var tx = SeedTransaction(h, payeeId, new DateOnly(2026, 6, 15), 1000m);
        SeedCredit(h, tx.Id, payeeId, plan.Id, 100m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(1);
    }

    [Fact]
    public async Task Terminating_one_payee_does_not_disturb_the_others_in_the_same_run()
    {
        var h = Build(nameof(Terminating_one_payee_does_not_disturb_the_others_in_the_same_run));
        var plan = SeedPlan(h);

        var leaver = SeedPayee(h, terminated: true, code: "GONE");
        SeedAssignment(h, leaver, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var leaverTx = SeedTransaction(h, leaver, new DateOnly(2026, 6, 15), 1000m);
        SeedCredit(h, leaverTx.Id, leaver, plan.Id, 100m);

        var stayer = SeedPayee(h, terminated: false, code: "HERE");
        SeedAssignment(h, stayer, plan.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var stayerTx = SeedTransaction(h, stayer, new DateOnly(2026, 6, 15), 2000m);
        SeedCredit(h, stayerTx.Id, stayer, plan.Id, 250m);

        var result = await h.Handler.Handle(June(), default);

        result.Value!.PayoutsCreated.Should().Be(1);
        var payout = await h.Db.CompensationPayouts.SingleAsync();
        payout.PayeeId.Should().Be(stayer);
        payout.TotalCommission.Amount.Should().Be(250m);
    }
}
