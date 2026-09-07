#pragma warning disable CS8602

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// Integration tests for anti-double-pay (Phase 3, WI-EXPLANATION-GAPS).
/// Verifies: credit consumption on MarkPaid, exclusion in overlapping calculations,
/// transaction status propagation, and revert (unconsume) flow.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class AntiDoublePayTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    // ── Test doubles ──────────────────────────────────────────────────────────


    // ══ KAN-52: la salida para un payout Approved que nunca podrá pagarse ═════════════════════

    private static DiscardPayoutHandler DiscardHandler(ApplicationDbContext db) =>
        new(db, new FixedCurrentUser(), new FakeClock(Now.UtcDateTime), AlwaysAllowAuth.Instance);

    /// <summary>
    /// Builds the exact shape the ticket describes: two payouts over overlapping periods for the same
    /// payee and plan. The first is paid — consuming the credits — and the second is left Approved,
    /// holding credits that can never be paid again.
    ///
    /// ★ IT IS SEEDED THE WAY PRODUCTION MADE IT. Measured in the real database, every stuck payout
    /// had this same origin: a period recalculated after a shorter one had already been paid. Seeding
    /// a payout with a hand-set "already consumed" flag would have tested the flag, not the situation.
    /// </summary>
    private async Task<(Guid stuckId, Guid payerId)> SeedStuckPayoutAsync(
        Guid tenantId, int creditCount = 2, int unpaidExtra = 0)
    {
        var planId = Guid.NewGuid();
        Guid stuckId;
        Guid payerId;

        await using var db = fixture.CreateDbForTenant(tenantId);

        var payee = MakePayee(tenantId, "EMP-STUCK");
        var plan = MakePlan(tenantId, planId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        db.Payees.Add(payee);
        db.CompensationPlans.Add(plan);
        db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

        var rule = plan.Rules.First();
        var snapshot = RuleSnapshot.Freeze(rule.Id, planId, 1, "Commission",
            RateTable.Flat(0.10m), Trigger.Always(), Now,
            measurement: new Measurement { Type = MeasurementType.Revenue });

        var shared = new List<Credit>();
        for (var i = 0; i < creditCount; i++)
        {
            var (tx, credit) = MakeTxWithCredit(tenantId, payee.Id, planId, rule.Id, snapshot,
                $"REF-SHARED-{i}", new DateOnly(2026, 3, 10), 1_000m);
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(credit);
            shared.Add(credit);
        }

        // Credits that ONLY the stuck payout carries — nobody has paid these.
        var extras = new List<Credit>();
        for (var i = 0; i < unpaidExtra; i++)
        {
            var (tx, credit) = MakeTxWithCredit(tenantId, payee.Id, planId, rule.Id, snapshot,
                $"REF-ONLY-{i}", new DateOnly(2026, 3, 20), 500m);
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(credit);
            extras.Add(credit);
        }

        CompensationPayout Build(IEnumerable<Credit> credits, DateOnly start, DateOnly end)
        {
            var specs = credits
                // ★ FRESH Money VALUES, not the credit's own. Money is an owned type already tracked
                // against the Credit; handing the same instance to a PayoutLine makes EF write NULL
                // into BaseAmount. The existing tests build new ones for exactly this reason.
                .Select(c => new PayoutLineSpec(
                    CreditId: c.Id,
                    RuleId: rule.Id,
                    RuleName: "Commission",
                    BaseAmount: Money.Of(c.OriginalAmount.Amount, c.OriginalAmount.Currency),
                    CommissionAmount: Money.Of(c.CreditedAmount.Amount, c.CreditedAmount.Currency),
                    AppliedModifiers: []))
                .ToList();

            return CompensationPayout.Calculate(
                tenantId, payee.Id, planId,
                PayeeReference.Snapshot(payee.Id, payee.FullName, payee.EmployeeCode),
                DateRange.Of(start, end), specs, Eur, "test",
                Guid.NewGuid(), Now, Guid.NewGuid(), Guid.NewGuid);
        }

        // The payer covers the SHORTER period and gets paid first.
        var payer = Build(shared, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15));
        payer.Approve("test", Now, Guid.NewGuid());
        db.CompensationPayouts.Add(payer);
        payerId = payer.Id;

        // The stuck one covers the LONGER period and picks the same credits up again.
        var stuck = Build(shared.Concat(extras), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        stuck.Approve("test", Now, Guid.NewGuid());
        db.CompensationPayouts.Add(stuck);
        stuckId = stuck.Id;

        await db.SaveChangesAsync();

        // Pay the first one for real: this is what consumes the credits.
        foreach (var credit in shared)
            credit.Consume(payerId, Now, Guid.NewGuid());

        payer.MarkPaid("test", Now);
        await db.SaveChangesAsync();

        return (stuckId, payerId);
    }

    /// <summary>
    /// The ticket's main acceptance criterion: the stuck payout gets a way out, reaches a terminal
    /// state, leaves the payable queue, and nothing that was already paid changes.
    /// </summary>
    [Fact]
    public async Task Discard_closes_an_unpayable_payout_without_touching_the_money_already_paid()
    {
        var tenantId = Guid.NewGuid();
        var (stuckId, payerId) = await SeedStuckPayoutAsync(tenantId, creditCount: 3);

        Result<DiscardPayoutResult> result;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            result = await DiscardHandler(db).Handle(
                new DiscardPayoutCommand(stuckId, "Period recalculated after the shorter one was paid."),
                CancellationToken.None);
        }

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.CreditsAlreadyPaidElsewhere.Should().Be(3);

        await using (var verify = fixture.CreateDbForTenant(tenantId))
        {
            var stuck = await verify.CompensationPayouts.FirstAsync(p => p.Id == stuckId);
            stuck.Status.Should().Be(CompensationPayoutStatus.Discarded);
            stuck.DiscardReason.Should().Be("Period recalculated after the shorter one was paid.");
            stuck.DiscardedBy.Should().Be("test-user");
            stuck.DiscardedAt.Should().NotBeNull();

            // ★★ NOTHING THAT WAS PAID MOVED. The payer keeps its status and its cash date, the
            // credits stay consumed BY IT, and no transaction was returned.
            var payer = await verify.CompensationPayouts.FirstAsync(p => p.Id == payerId);
            payer.Status.Should().Be(CompensationPayoutStatus.Paid);
            payer.PaidAt.Should().NotBeNull();

            var consumed = await verify.Credits
                .Where(c => c.ConsumedByPayoutId == payerId)
                .CountAsync();
            consumed.Should().Be(3, "the credits stay consumed by the payout that really paid them");
        }
    }

    /// <summary>
    /// ★★ THE MONEY GUARD, AND THE REASON THIS TEST EXISTS. Measured in the real database, three of
    /// the five stuck payouts were only PARTIALLY blocked — one held 71 credits already paid and 139
    /// that nobody had paid, the larger part of €34,567.64. Discarding that would retire a real debt
    /// to a real person on the strength of a button whose label says the money was already paid.
    /// </summary>
    [Fact]
    public async Task Discard_refuses_a_payout_that_still_carries_unpaid_commission()
    {
        var tenantId = Guid.NewGuid();
        var (stuckId, _) = await SeedStuckPayoutAsync(tenantId, creditCount: 2, unpaidExtra: 3);

        Result<DiscardPayoutResult> result;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            result = await DiscardHandler(db).Handle(
                new DiscardPayoutCommand(stuckId, "Looks stuck."), CancellationToken.None);
        }

        result.IsSuccess.Should().BeFalse("3 of its credits have not been paid by anybody");
        result.Error.Should().Contain("3");

        await using (var verify = fixture.CreateDbForTenant(tenantId))
        {
            var stuck = await verify.CompensationPayouts.FirstAsync(p => p.Id == stuckId);
            stuck.Status.Should().Be(CompensationPayoutStatus.Approved, "a refused discard changes nothing");
            stuck.DiscardedAt.Should().BeNull();
        }
    }

    /// <summary>
    /// ★ A PAID PAYOUT IS NOT DISCARDABLE. Discarding one would erase the record of a payment while
    /// the cash stayed gone; reversing a payment is RevertPaidToApproved, a different operation.
    /// </summary>
    [Fact]
    public async Task Discard_refuses_a_payout_that_was_already_paid()
    {
        var tenantId = Guid.NewGuid();
        var (_, payerId) = await SeedStuckPayoutAsync(tenantId);

        Result<DiscardPayoutResult> result;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            result = await DiscardHandler(db).Handle(
                new DiscardPayoutCommand(payerId, "Trying to erase a payment."), CancellationToken.None);
        }

        result.IsSuccess.Should().BeFalse();

        await using (var verify = fixture.CreateDbForTenant(tenantId))
        {
            var payer = await verify.CompensationPayouts.FirstAsync(p => p.Id == payerId);
            payer.Status.Should().Be(CompensationPayoutStatus.Paid);
            payer.PaidAt.Should().NotBeNull("the cash date survives a refused discard");
        }
    }

    /// <summary>
    /// ★ THE REASON IS A SERVER INVARIANT, NOT ONLY A DISABLED BUTTON (§D2). The endpoint is reachable
    /// without the modal, and a discard with no stated reason is the row an auditor would ask about.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Discard_refuses_without_a_stated_reason(string reason)
    {
        var tenantId = Guid.NewGuid();
        var (stuckId, _) = await SeedStuckPayoutAsync(tenantId);

        Result<DiscardPayoutResult> result;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            result = await DiscardHandler(db).Handle(
                new DiscardPayoutCommand(stuckId, reason), CancellationToken.None);
        }

        result.IsSuccess.Should().BeFalse();

        await using (var verify = fixture.CreateDbForTenant(tenantId))
        {
            var stuck = await verify.CompensationPayouts.FirstAsync(p => p.Id == stuckId);
            stuck.Status.Should().Be(CompensationPayoutStatus.Approved);
        }
    }

    /// <summary>
    /// ★★ AND IT LEAVES THE QUEUE. The point of the whole ticket: the payable list is what the ticket
    /// says these payouts were clogging. Asserted against the real list query rather than by reading
    /// the status back, because "it has a terminal status" and "it stopped being listed" are two
    /// different claims and only the second one is what the user complained about.
    /// </summary>
    [Fact]
    public async Task A_discarded_payout_no_longer_appears_among_the_approved()
    {
        var tenantId = Guid.NewGuid();
        var (stuckId, _) = await SeedStuckPayoutAsync(tenantId);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var approvedBefore = await db.CompensationPayouts
                .CountAsync(p => p.Status == CompensationPayoutStatus.Approved);
            approvedBefore.Should().Be(1);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await DiscardHandler(db).Handle(
                new DiscardPayoutCommand(stuckId, "Already paid by the shorter period."),
                CancellationToken.None);
            r.IsSuccess.Should().BeTrue(r.Error);
        }

        await using (var verify = fixture.CreateDbForTenant(tenantId))
        {
            var approvedAfter = await verify.CompensationPayouts
                .CountAsync(p => p.Status == CompensationPayoutStatus.Approved);
            approvedAfter.Should().Be(0, "the queue of payouts waiting to be paid is finally clean");
        }
    }


    /// <summary>
    /// ★★ THE STATEMENT NOW ANSWERS "is this wholly or partly duplicated?" WITHOUT PRESSING ANYTHING.
    /// The server has always known which credits another payout consumed — it is the payment guard —
    /// but the statement did not show it, so the only way to find out was to press Discard and read
    /// the refusal. Asserted on the DTO the screen actually receives, and with a MIXED payout, because
    /// a payout where every line says the same thing would pass even if the flag were hard-coded.
    /// </summary>
    [Fact]
    public async Task The_statement_says_which_lines_another_payout_already_paid()
    {
        var tenantId = Guid.NewGuid();
        var (stuckId, payerId) = await SeedStuckPayoutAsync(tenantId, creditCount: 2, unpaidExtra: 3);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var stuck = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstAsync(p => p.Id == stuckId);

        var lines = await GetPayoutByIdHandler.BuildLinesAsync(
            stuck.Lines, db, stuck.Id, CancellationToken.None);

        lines.Count(l => l.PaidInPayoutId != null).Should().Be(2);
        lines.Count(l => l.PaidInPayoutId == null).Should().Be(3, "nobody has paid these three");

        var duplicated = lines.First(l => l.PaidInPayoutId != null);
        duplicated.PaidInPayoutId.Should().Be(payerId);
        duplicated.PaidInPayoutPeriodStart.Should().Be(new DateOnly(2026, 3, 1));
        duplicated.PaidInPayoutPeriodEnd.Should().Be(new DateOnly(2026, 3, 15),
            "the period is what shows the overlap that created the duplicate");
    }

    /// <summary>
    /// ★★ AND A PAYOUT DOES NOT ACCUSE ITSELF. Once a payout is paid, every one of its credits carries
    /// a ConsumedByPayoutId — its own. Without comparing against the payout being rendered, every line
    /// of every paid statement would claim to be a duplicate of itself.
    /// </summary>
    [Fact]
    public async Task A_paid_payout_does_not_report_its_own_lines_as_paid_elsewhere()
    {
        var tenantId = Guid.NewGuid();
        var (_, payerId) = await SeedStuckPayoutAsync(tenantId, creditCount: 3);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var payer = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstAsync(p => p.Id == payerId);

        payer.Status.Should().Be(CompensationPayoutStatus.Paid);

        var lines = await GetPayoutByIdHandler.BuildLinesAsync(
            payer.Lines, db, payer.Id, CancellationToken.None);

        lines.Should().OnlyContain(l => l.PaidInPayoutId == null,
            "these credits were consumed by THIS payout, which is not a duplicate");

        // ★★ AND THE STATE SAYS "PAID", NOT "UNPAID". This is the bug the binary version shipped: the
        // absence of ANOTHER payout's id was read as nobody having paid, so a settled statement
        // called every one of its own lines unpaid while the Transactions list called the same
        // transactions Paid. Asserting the id alone would still pass today — the state is the claim.
        lines.Should().OnlyContain(l => l.PaymentState == PayoutLinePaymentState.PaidByThisPayout,
            "this payout paid them, so the statement must say so");
    }

    /// <summary>
    /// ★★ THE THREE STATES ON ONE STATEMENT, which is the only arrangement that can catch a mapping
    /// that collapses two of them. A payout carrying only duplicates, or only unpaid lines, passes
    /// even when the rule behind the badge is wrong.
    /// </summary>
    [Fact]
    public async Task A_statement_tells_paid_here_from_paid_elsewhere_from_unpaid()
    {
        var tenantId = Guid.NewGuid();
        var (stuckId, payerId) = await SeedStuckPayoutAsync(tenantId, creditCount: 2, unpaidExtra: 3);

        await using var db = fixture.CreateDbForTenant(tenantId);

        // The stuck payout: 2 credits another payout paid, 3 nobody has.
        var stuck = await db.CompensationPayouts.Include(p => p.Lines).FirstAsync(p => p.Id == stuckId);
        var stuckLines = await GetPayoutByIdHandler.BuildLinesAsync(
            stuck.Lines, db, stuck.Id, CancellationToken.None);

        stuckLines.Count(l => l.PaymentState == PayoutLinePaymentState.PaidByAnotherPayout).Should().Be(2);
        stuckLines.Count(l => l.PaymentState == PayoutLinePaymentState.Unpaid).Should().Be(3);
        stuckLines.Should().NotContain(l => l.PaymentState == PayoutLinePaymentState.PaidByThisPayout,
            "this payout has paid nothing");

        // The payer: the same two credits, settled by itself.
        var payer = await db.CompensationPayouts.Include(p => p.Lines).FirstAsync(p => p.Id == payerId);
        var payerLines = await GetPayoutByIdHandler.BuildLinesAsync(
            payer.Lines, db, payer.Id, CancellationToken.None);

        payerLines.Should().OnlyContain(l => l.PaymentState == PayoutLinePaymentState.PaidByThisPayout);
    }

    private sealed class AlwaysAllowAuth : IAuthorizationService
    {
        public static readonly AlwaysAllowAuth Instance = new();
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
        // Added with IAuthorizationService.HasAsync: this double allows everything, so the
        // question answers the same way the enforcement does.
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public static readonly NoOpAuditService Instance = new();
        public Task LogAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? Email => "test@test.com";
        public bool IsAuthenticated => true;
    }

    // ── Handler factories ─────────────────────────────────────────────────────

    private static CalculatePayoutsForPeriodHandler CalcHandler(ApplicationDbContext db, Guid tenantId) =>
        new(db, new PayoutEngineFixture.FixedTenantContext(tenantId),
            new FixedCurrentUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            NullLogger<CalculatePayoutsForPeriodHandler>.Instance);

    private static MarkPayoutPaidHandler PaidHandler(ApplicationDbContext db) =>
        new(db, AlwaysAllowAuth.Instance, new FixedCurrentUser(),
            new FakeClock(Now.UtcDateTime), NoOpAuditService.Instance,
            NullLogger<MarkPayoutPaidHandler>.Instance);

    private static RevertPayoutToApprovedHandler RevertHandler(ApplicationDbContext db) =>
        new(db, AlwaysAllowAuth.Instance, new FixedCurrentUser(),
            new FakeClock(Now.UtcDateTime), NoOpAuditService.Instance,
            NullLogger<RevertPayoutToApprovedHandler>.Instance);

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Payee MakePayee(Guid tenantId, string code) =>
        Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);

    private static Plan MakePlan(Guid tenantId, Guid planId, DateOnly start, DateOnly end)
    {
        var plan = Plan.Create(tenantId, "Test Plan", "desc", DateRange.Of(start, end), Eur,
            "test", planId, Now, Guid.NewGuid());
        plan.AddRule("Commission", 1,
            new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            RateTable.Flat(0.10m));
        return plan;
    }

    private static PlanAssignment MakeAssignment(Guid tenantId, Guid planId, Payee payee,
        DateOnly start, DateOnly end)
    {
        var snapshot = PayeeReference.Snapshot(payee.Id, payee.FullName, payee.EmployeeCode);
        return PlanAssignment.Create(tenantId, planId, payee.Id, snapshot,
            DateRange.Of(start, end), "test", Guid.NewGuid(), Now, Guid.NewGuid());
    }

    private static (CompensationTransaction tx, Credit credit) MakeTxWithCredit(
        Guid tenantId, Guid payeeId, Guid planId, Guid ruleId, RuleSnapshot snapshot,
        string refNum, DateOnly date, decimal amount)
    {
        var tx = CompensationTransaction.Ingest(tenantId, refNum, payeeId,
            Money.Of(amount, Eur), date, TransactionSource.Manual, "test",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var credit = Credit.Allocate(tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
            Money.Of(amount, Eur), Money.Of(amount * 0.10m, Eur),
            Percentage.FromPercent(100), CreditRole.Primary,
            "test", Guid.NewGuid(), Now, Guid.NewGuid());

        tx.MarkCalculated(1, Money.Of(amount * 0.10m, Eur), "test", Now, Guid.NewGuid());

        return (tx, credit);
    }

    // ── TEST 1: MarkPaid propagates to transactions and consumes credits ───────

    [Fact]
    public async Task MarkPaid_ConsumesCreditsAndMarksTransactionsPaid()
    {
        var tenantId = Guid.NewGuid();
        var planId   = Guid.NewGuid();
        var start    = new DateOnly(2026, 1, 1);
        var end      = new DateOnly(2026, 3, 31);
        Guid payeeId = Guid.Empty;
        Guid payoutId = Guid.Empty;
        Guid creditId = Guid.Empty;
        Guid txId     = Guid.Empty;

        // Seed: payee, plan, assignment, tx+credit.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-PAID-01");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx, credit) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-PAID-01", new DateOnly(2026, 2, 1), 1000m);
            txId     = tx.Id;
            creditId = credit.Id;
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(credit);
            await db.SaveChangesAsync();
        }

        // Calculate payout.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
            result.IsSuccess.Should().BeTrue();
            result.Value!.PayoutsCreated.Should().Be(1);
        }

        // Approve and mark paid.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeId);
            payoutId = payout.Id;
            payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutId), default);
            result.IsSuccess.Should().BeTrue();
        }

        // Assert: credit consumed, transaction paid.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var credit = await db.Credits.IgnoreQueryFilters().FirstAsync(c => c.Id == creditId);
            credit.ConsumedAt.Should().NotBeNull("credit must be consumed after payout is paid");
            credit.ConsumedByPayoutId.Should().Be(payoutId);

            var tx = await db.CompensationTransactions.IgnoreQueryFilters().FirstAsync(t => t.Id == txId);
            tx.Status.Should().Be(CompensationTransactionStatus.Paid,
                "transaction must be Paid after its payout is paid");
        }
    }

    // ── TEST 2: THE ANTI-DOUBLE-PAY TEST ─────────────────────────────────────
    // Pay period A (Jun 1-30). Then calculate period B (Jun 15 - Jul 15).
    // Credits from Jun 15-30 must NOT appear in period B (they are consumed by A).

    [Fact]
    public async Task OverlappingPeriod_ExcludesConsumedCreditsFromPriorPaidPeriod()
    {
        var tenantId  = Guid.NewGuid();
        var planId    = Guid.NewGuid();
        var junStart  = new DateOnly(2026, 6, 1);
        var junEnd    = new DateOnly(2026, 6, 30);
        var overlapStart = new DateOnly(2026, 6, 15);
        var overlapEnd   = new DateOnly(2026, 7, 15);
        Guid payeeId  = Guid.Empty;
        Guid payoutAId = Guid.Empty;

        // Seed: payee, plan Jun 1 – Jul 31, assignment, two transactions:
        //   - Jun 10 (in period A only)
        //   - Jun 20 (in both periods A and B)
        //   - Jul 5  (in period B only)
        var planStart = new DateOnly(2026, 6, 1);
        var planEnd   = new DateOnly(2026, 7, 31);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-OVLP");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, planStart, planEnd);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, planStart, planEnd));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx1, c1) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-OVLP-01", new DateOnly(2026, 6, 10), 500m);   // period A only
            var (tx2, c2) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-OVLP-02", new DateOnly(2026, 6, 20), 800m);   // overlap: A and B
            var (tx3, c3) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-OVLP-03", new DateOnly(2026, 7, 5), 600m);    // period B only

            db.CompensationTransactions.AddRange(tx1, tx2, tx3);
            db.Credits.AddRange(c1, c2, c3);
            await db.SaveChangesAsync();
        }

        // ── Step 1: calculate + approve + pay period A (Jun 1-30) ─────────────
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(junStart, junEnd), default);
            r.IsSuccess.Should().BeTrue();
            r.Value!.PayoutsCreated.Should().Be(1);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts
                .Include(p => p.Lines)
                .FirstAsync(p => p.PayeeId == payeeId);
            payoutAId = payout.Id;
            // Period A should include tx1 (Jun 10) and tx2 (Jun 20) = 2 lines.
            payout.Lines.Should().HaveCount(2, "period A covers Jun 1-30: tx1 (Jun 10) and tx2 (Jun 20)");
            payout.TotalCommission.Amount.Should().Be(130m, "500*0.10 + 800*0.10 = 130");
            payout.Approve("approver", Now.AddHours(1), Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutAId), default);
            result.IsSuccess.Should().BeTrue();
        }

        // ── Step 2: calculate period B (Jun 15 - Jul 15) ─────────────────────
        // Credits for Jun 20 (tx2) must be EXCLUDED because period A consumed them.
        // Only Jul 5 (tx3) should be included.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(overlapStart, overlapEnd), default);
            r.IsSuccess.Should().BeTrue();
            r.Value!.PayoutsCreated.Should().Be(1);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payoutB = await db.CompensationPayouts
                .Include(p => p.Lines)
                .FirstAsync(p => p.PayeeId == payeeId && p.Id != payoutAId);

            payoutB.Lines.Should().HaveCount(1,
                "period B covers Jun 15-Jul 15: tx2 (Jun 20) is consumed by A, only tx3 (Jul 5) remains");
            payoutB.TotalCommission.Amount.Should().Be(60m,
                "only tx3 contributes: 600 * 0.10 = 60 (tx2's 80 must NOT be re-included)");
        }
    }

    // ── TEST 3: Non-overlapping period works normally ─────────────────────────

    [Fact]
    public async Task NonOverlappingPeriod_IncludesAllAvailableCredits()
    {
        var tenantId = Guid.NewGuid();
        var planId   = Guid.NewGuid();
        var q1Start  = new DateOnly(2026, 1, 1);
        var q1End    = new DateOnly(2026, 3, 31);
        var q2Start  = new DateOnly(2026, 4, 1);
        var q2End    = new DateOnly(2026, 6, 30);
        Guid payeeId  = Guid.Empty;
        Guid payout1Id = Guid.Empty;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-NOVLP");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, q1Start, q2End);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, q1Start, q2End));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx1, c1) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-Q1-01", new DateOnly(2026, 2, 15), 1000m);
            var (tx2, c2) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-Q2-01", new DateOnly(2026, 5, 15), 2000m);

            db.CompensationTransactions.AddRange(tx1, tx2);
            db.Credits.AddRange(c1, c2);
            await db.SaveChangesAsync();
        }

        // Calculate + pay Q1.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(q1Start, q1End), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var p = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeId);
            payout1Id = p.Id;
            p.Approve("approver", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payout1Id), default);
        }

        // Calculate Q2 — should include Q2 credit normally.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(q2Start, q2End), default);
            r.IsSuccess.Should().BeTrue();
            r.Value!.PayoutsCreated.Should().Be(1);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payoutQ2 = await db.CompensationPayouts
                .Include(p => p.Lines)
                .FirstAsync(p => p.PayeeId == payeeId && p.Id != payout1Id);

            payoutQ2.Lines.Should().HaveCount(1, "Q2 credit is available — not consumed by Q1 payout");
            payoutQ2.TotalCommission.Amount.Should().Be(200m, "2000 * 0.10 = 200");
        }
    }

    // ── TEST 4: Revert paid payout unconsuemes credits and reverts transactions ─

    [Fact]
    public async Task RevertPaid_UnconsumesCreditAndRevertsTransactionToCalculated()
    {
        var tenantId = Guid.NewGuid();
        var planId   = Guid.NewGuid();
        var start    = new DateOnly(2026, 1, 1);
        var end      = new DateOnly(2026, 3, 31);
        Guid payeeId  = Guid.Empty;
        Guid payoutId = Guid.Empty;
        Guid creditId = Guid.Empty;
        Guid txId     = Guid.Empty;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-REVT");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx, credit) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-REVT-01", new DateOnly(2026, 2, 1), 1000m);
            txId     = tx.Id;
            creditId = credit.Id;
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(credit);
            await db.SaveChangesAsync();
        }

        // Calculate → Approve → Paid.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeId);
            payoutId = payout.Id;
            payout.Approve("approver", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutId), default);
        }

        // Verify paid state before revert.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var credit = await db.Credits.IgnoreQueryFilters().FirstAsync(c => c.Id == creditId);
            credit.ConsumedAt.Should().NotBeNull();
            var tx = await db.CompensationTransactions.IgnoreQueryFilters().FirstAsync(t => t.Id == txId);
            tx.Status.Should().Be(CompensationTransactionStatus.Paid);
        }

        // Revert the paid payout.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await RevertHandler(db)
                .Handle(new RevertPayoutToApprovedCommand(payoutId), default);
            result.IsSuccess.Should().BeTrue();
        }

        // Assert: payout back to Approved, credit unconsumed, transaction back to Calculated.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payout = await db.CompensationPayouts.FirstAsync(p => p.Id == payoutId);
            payout.Status.Should().Be(CompensationPayoutStatus.Approved,
                "reverted payout must be Approved");

            var credit = await db.Credits.IgnoreQueryFilters().FirstAsync(c => c.Id == creditId);
            credit.ConsumedAt.Should().BeNull("credit must be unconsumed after payout revert");
            credit.ConsumedByPayoutId.Should().BeNull();

            var tx = await db.CompensationTransactions.IgnoreQueryFilters().FirstAsync(t => t.Id == txId);
            tx.Status.Should().Be(CompensationTransactionStatus.Calculated,
                "transaction must revert to Calculated when its payout is reverted");
        }
    }

    // ── TEST 5: After revert, credits are available for recalculation ─────────

    [Fact]
    public async Task AfterRevert_CreditsAreAvailableForRecalculation()
    {
        var tenantId = Guid.NewGuid();
        var planId   = Guid.NewGuid();
        var start    = new DateOnly(2026, 1, 1);
        var end      = new DateOnly(2026, 3, 31);
        Guid payeeId  = Guid.Empty;
        Guid payoutId = Guid.Empty;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-RECALC");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx, credit) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "REF-RCALC-01", new DateOnly(2026, 2, 1), 500m);
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(credit);
            await db.SaveChangesAsync();
        }

        // Calculate → Approve → Pay → Revert.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var p = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeId);
            payoutId = p.Id;
            p.Approve("approver", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutId), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await RevertHandler(db).Handle(new RevertPayoutToApprovedCommand(payoutId), default);
        }

        // Payout is now Approved — revert it to Calculated so we can recalculate.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var p = await db.CompensationPayouts.FirstAsync(p => p.Id == payoutId);
            p.RevertToCalculated("test", Now);
            await db.SaveChangesAsync();
        }

        // Now remove the old Calculated payout so the engine can create a fresh one.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var old = await db.CompensationPayouts.FirstAsync(p => p.Id == payoutId);
            db.CompensationPayouts.Remove(old);
            await db.SaveChangesAsync();
        }

        // Recalculate — credits should now be available (unconsumed).
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var r = await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
            r.IsSuccess.Should().BeTrue();
            r.Value!.PayoutsCreated.Should().Be(1,
                "unconsumed credits are available again after payout revert");
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var newPayout = await db.CompensationPayouts
                .Include(p => p.Lines)
                .FirstAsync(p => p.PayeeId == payeeId);

            newPayout.Lines.Should().HaveCount(1, "credit is available again after revert");
            newPayout.TotalCommission.Amount.Should().Be(50m, "500 * 0.10 = 50");
        }
    }

    // ── TEST 7: Paying second payout with same consumed credit → BLOCKED ─────────
    // Reproduces the SMOCK-326546143213 scenario: both payouts calculated before
    // any payment → same credit in both PayoutLines → paying A consumes credit →
    // paying B must BLOCK (not silently skip and still pay).

    [Fact]
    public async Task PayingPayout_WithAlreadyConsumedCredit_IsBlocked_WithClearError()
    {
        var tenantId = Guid.NewGuid();
        var planId   = Guid.NewGuid();
        var start    = new DateOnly(2026, 1, 1);
        var end      = new DateOnly(2026, 3, 31);
        Guid payeeId  = Guid.Empty;
        Guid payoutAId = Guid.Empty;
        Guid payoutBId = Guid.Empty;

        // Seed payee + plan covering Jan-Mar, assignment covering Jan-Mar.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-BLCK");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            // One transaction on Jan 15 — will be captured by BOTH payout periods below.
            var (tx, credit) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "SMOCK-DBLPAY-001", new DateOnly(2026, 1, 15), 1000m);
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(credit);
            await db.SaveChangesAsync();
        }

        // Calculate payout A (Jan 1-31) — captures credit for Jan 15.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, new DateOnly(2026, 1, 31)), default);
        }

        // Calculate payout B (Jan 1-Mar 31) — credit NOT yet consumed → ALSO captured.
        // This is the vulnerability window: both payouts calculated before any payment.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
        }

        // Verify both payouts exist with the same credit.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var allPayouts = await db.CompensationPayouts.Include(p => p.Lines)
                .Where(p => p.PayeeId == payeeId).ToListAsync();
            allPayouts.Should().HaveCount(2, "two payouts for different periods");

            var creditIds = allPayouts.SelectMany(p => p.Lines.Select(l => l.CreditId)).ToList();
            creditIds.Distinct().Should().HaveCount(1, "both payouts reference the SAME credit");

            var payoutA = allPayouts.First(p => p.Period.End == new DateOnly(2026, 1, 31));
            var payoutB = allPayouts.First(p => p.Period.End == end);
            payoutAId = payoutA.Id;
            payoutBId = payoutB.Id;

            // Approve both.
            payoutA.Approve("test", Now, Guid.NewGuid());
            payoutB.Approve("test", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        // Pay payout A — must succeed and consume the credit.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutAId), default);
            result.IsSuccess.Should().BeTrue("payout A should pay successfully");
        }

        // Try to pay payout B — must be BLOCKED because the credit is consumed.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutBId), default);
            result.IsSuccess.Should().BeTrue("block is a domain outcome, not a handler error");
            result.Value.Should().NotBeNull("blocked result carries structured conflict data");
            result.Value!.TotalConflicts.Should().BeGreaterThan(0, "at least one conflict must be reported");
            result.Value.Conflicts.Should().Contain(c => c.TransactionReference.Contains("SMOCK-DBLPAY-001"),
                "conflict must identify the double-paid transaction");
        }

        // Verify payout B is still Approved (not Paid) — the block was effective.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payoutB = await db.CompensationPayouts.FirstAsync(p => p.Id == payoutBId);
            payoutB.Status.Should().Be(CompensationPayoutStatus.Approved,
                "payout B must remain Approved — no double payment occurred");
        }
    }

    // ── TEST 8: After revert, blocked payout can now be paid ─────────────────

    [Fact]
    public async Task AfterPaymentBlock_RevertFirstPayout_SecondPayoutCanBePaid()
    {
        var tenantId = Guid.NewGuid();
        var planId   = Guid.NewGuid();
        var start    = new DateOnly(2026, 1, 1);
        var end      = new DateOnly(2026, 3, 31);
        Guid payeeId  = Guid.Empty;
        Guid payoutAId = Guid.Empty;
        Guid payoutBId = Guid.Empty;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-RBLCK");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx, credit) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "SMOCK-REVERT-001", new DateOnly(2026, 1, 15), 2000m);
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(credit);
            await db.SaveChangesAsync();
        }

        // Calculate both periods before paying either.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, new DateOnly(2026, 1, 31)), default);
        }
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payouts = await db.CompensationPayouts.Include(p => p.Lines)
                .Where(p => p.PayeeId == payeeId).ToListAsync();
            var payoutA = payouts.First(p => p.Period.End == new DateOnly(2026, 1, 31));
            var payoutB = payouts.First(p => p.Period.End == end);
            payoutAId = payoutA.Id;
            payoutBId = payoutB.Id;
            payoutA.Approve("test", Now, Guid.NewGuid());
            payoutB.Approve("test", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        // Pay A, verify B is blocked, revert A, then B should succeed.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutAId), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var blocked = await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutBId), default);
            blocked.IsSuccess.Should().BeTrue("block is a structured outcome, not a handler error");
            blocked.Value.Should().NotBeNull("blocked result carries conflict data");
        }

        // Revert A — credit is unconsumed.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await RevertHandler(db).Handle(new RevertPayoutToApprovedCommand(payoutAId), default);
        }

        // Now B should pay successfully (credit is available again).
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var result = await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutBId), default);
            result.IsSuccess.Should().BeTrue("after reverting A, credit is free and B can be paid");
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payoutB = await db.CompensationPayouts.FirstAsync(p => p.Id == payoutBId);
            payoutB.Status.Should().Be(CompensationPayoutStatus.Paid,
                "payout B must now be Paid after credit was freed by reverting A");
        }
    }

    // ── TEST 9: BulkMarkPaid — one blocked, others proceed ────────────────────

    [Fact]
    public async Task BulkMarkPaid_WithOneConsumedCredit_BlocksConflictAndAllowsCleanPayouts()
    {
        var tenantId = Guid.NewGuid();
        var planId   = Guid.NewGuid();
        var start    = new DateOnly(2026, 1, 1);
        var end      = new DateOnly(2026, 3, 31);
        Guid payeeId  = Guid.Empty;
        Guid payoutConflictId = Guid.Empty;
        Guid payoutCleanId    = Guid.Empty;
        Guid payoutFirstId    = Guid.Empty;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = MakePayee(tenantId, "EMP-BULK");
            payeeId = payee.Id;
            var plan = MakePlan(tenantId, planId, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantId, planId, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
            var ruleId = plan2.Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            // tx1 on Jan 15 — shared credit between payoutFirst and payoutConflict.
            var (tx1, c1) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "BULK-SHARED-001", new DateOnly(2026, 1, 15), 1000m);
            // tx2 on Feb 15 — only in payoutClean (Feb 1-Mar 31).
            var (tx2, c2) = MakeTxWithCredit(tenantId, payeeId, planId, ruleId, snapshot,
                "BULK-CLEAN-001", new DateOnly(2026, 2, 15), 500m);
            db.CompensationTransactions.AddRange(tx1, tx2);
            db.Credits.AddRange(c1, c2);
            await db.SaveChangesAsync();
        }

        // payoutFirst: Jan 1-31 (has c1 for Jan 15 tx).
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, new DateOnly(2026, 1, 31)), default);
        }

        // payoutConflict: Jan 1-Mar 31 (ALSO has c1, not yet consumed) + c2 (Feb 15).
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
        }

        // payoutClean: Feb 1-Mar 31 (only c2 — no conflict).
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await CalcHandler(db, tenantId)
                .Handle(new CalculatePayoutsForPeriodCommand(new DateOnly(2026, 2, 1), end), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payouts = await db.CompensationPayouts.Include(p => p.Lines)
                .Where(p => p.PayeeId == payeeId).ToListAsync();

            var pFirst    = payouts.First(p => p.Period.Start == start && p.Period.End == new DateOnly(2026, 1, 31));
            var pConflict = payouts.First(p => p.Period.Start == start && p.Period.End == end);
            var pClean    = payouts.First(p => p.Period.Start == new DateOnly(2026, 2, 1));
            payoutFirstId    = pFirst.Id;
            payoutConflictId = pConflict.Id;
            payoutCleanId    = pClean.Id;

            pFirst.Approve("test", Now, Guid.NewGuid());
            pConflict.Approve("test", Now, Guid.NewGuid());
            pClean.Approve("test", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        // Pay payoutFirst first — consumes c1.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutFirstId), default);
        }

        // BulkMarkPaid: payoutConflict (blocked) + payoutClean (should succeed).
        Result<BulkMarkPaidResult> bulkResult;
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var bulkHandler = new BulkMarkPaidHandler(
                db,
                new AlwaysAllowAuth(),
                new FixedCurrentUser(),
                new FakeClock(Now.UtcDateTime),
                NoOpAuditService.Instance,
                NullLogger<BulkMarkPaidHandler>.Instance);

            bulkResult = await bulkHandler.Handle(
                new BulkMarkPaidCommand([payoutConflictId, payoutCleanId]), default);
        }

        bulkResult.IsSuccess.Should().BeTrue("bulk returns success even with partial errors");
        bulkResult.Value!.Paid.Should().Be(1, "only payoutClean paid — payoutConflict was blocked");
        bulkResult.Value.Errors.Should().HaveCount(1, "one error for the blocked payout");
        bulkResult.Value.Errors[0].Should().Contain(payoutConflictId.ToString()[..8],
            "error must identify the blocked payout");

        // Verify payoutConflict is still Approved.
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var pConflict = await db.CompensationPayouts.FirstAsync(p => p.Id == payoutConflictId);
            pConflict.Status.Should().Be(CompensationPayoutStatus.Approved,
                "blocked payout must remain Approved");

            var pClean = await db.CompensationPayouts.FirstAsync(p => p.Id == payoutCleanId);
            pClean.Status.Should().Be(CompensationPayoutStatus.Paid,
                "clean payout must be Paid");
        }
    }

    // ── TEST 6: Multi-tenant isolation — consumption does not cross tenants ────

    [Fact]
    public async Task CreditConsumption_IsTenantIsolated()
    {
        var tenantA  = Guid.NewGuid();
        var tenantB  = Guid.NewGuid();
        var planIdA  = Guid.NewGuid();
        var planIdB  = Guid.NewGuid();
        var start    = new DateOnly(2026, 1, 1);
        var end      = new DateOnly(2026, 3, 31);
        Guid payeeAId = Guid.Empty;
        Guid payeeBId = Guid.Empty;
        Guid payoutAId = Guid.Empty;
        Guid creditBId = Guid.Empty;

        // Seed tenant A.
        await using (var db = fixture.CreateDbForTenant(tenantA))
        {
            var payee = MakePayee(tenantA, "EMP-A");
            payeeAId = payee.Id;
            var plan = MakePlan(tenantA, planIdA, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantA, planIdA, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planIdA);
            var ruleId = plan2.Rules.First().Id;
            var snap = RuleSnapshot.Freeze(ruleId, planIdA, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx, c) = MakeTxWithCredit(tenantA, payeeAId, planIdA, ruleId, snap,
                "REF-TA-01", new DateOnly(2026, 2, 1), 1000m);
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(c);
            await db.SaveChangesAsync();
        }

        // Seed tenant B with same plan dates and transaction date.
        await using (var db = fixture.CreateDbForTenant(tenantB))
        {
            var payee = MakePayee(tenantB, "EMP-B");
            payeeBId = payee.Id;
            var plan = MakePlan(tenantB, planIdB, start, end);
            db.CompensationPlans.Add(plan);
            db.Payees.Add(payee);
            db.PlanAssignments.Add(MakeAssignment(tenantB, planIdB, payee, start, end));
            await db.SaveChangesAsync();

            var plan2 = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planIdB);
            var ruleId = plan2.Rules.First().Id;
            var snap = RuleSnapshot.Freeze(ruleId, planIdB, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var (tx, c) = MakeTxWithCredit(tenantB, payeeBId, planIdB, ruleId, snap,
                "REF-TB-01", new DateOnly(2026, 2, 1), 2000m);
            creditBId = c.Id;
            db.CompensationTransactions.Add(tx);
            db.Credits.Add(c);
            await db.SaveChangesAsync();
        }

        // Calculate and pay tenant A.
        await using (var db = fixture.CreateDbForTenant(tenantA))
        {
            await CalcHandler(db, tenantA)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
        }

        await using (var db = fixture.CreateDbForTenant(tenantA))
        {
            var p = await db.CompensationPayouts.FirstAsync(p => p.PayeeId == payeeAId);
            payoutAId = p.Id;
            p.Approve("approver", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(tenantA))
        {
            await PaidHandler(db).Handle(new MarkPayoutPaidCommand(payoutAId), default);
        }

        // Tenant B's credit must NOT be consumed.
        await using (var db = fixture.CreateDbForTenant(tenantB))
        {
            var creditB = await db.Credits.IgnoreQueryFilters().FirstAsync(c => c.Id == creditBId);
            creditB.ConsumedAt.Should().BeNull(
                "tenant B's credit must not be consumed by tenant A's payout");
        }

        // Calculate tenant B — should work normally.
        await using (var db = fixture.CreateDbForTenant(tenantB))
        {
            var r = await CalcHandler(db, tenantB)
                .Handle(new CalculatePayoutsForPeriodCommand(start, end), default);
            r.IsSuccess.Should().BeTrue();
            r.Value!.PayoutsCreated.Should().Be(1, "tenant B is unaffected by tenant A's payments");
        }
    }
}
