#pragma warning disable CS8602

using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.Handlers.PayRuns;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Compensation;

/// <summary>
/// The clawback settlement against REAL SQL: a payee's outstanding balance is withheld from what a
/// pay run actually pays, limited by each plan's cap, with the remainder carried over.
///
/// These have to be integration tests, not unit tests: the OCC guard on PayeeBalance is a SQL
/// Server <c>rowversion</c>, and EF InMemory neither generates nor checks it — the concurrency
/// assertions below would pass vacuously there.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class ClawbackSettlementIntegrationTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";
    private static readonly DateOnly PeriodStart = new(2026, 1, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    // ── Test doubles (same shape as PayRunEngineTests) ────────────────────────

    private sealed class AlwaysAllowAuth : IAuthorizationService
    {
        public static readonly AlwaysAllowAuth Instance = new();
        public Task RequireAsync(string permission, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public static readonly NoOpAuditService Instance = new();
        public Task LogAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedUser : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? Email => "test@test.com";
        public bool IsAuthenticated => true;
    }

    private sealed class DirectSender(ApplicationDbContext db, Guid tenantId) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is CalculatePayoutsForPeriodCommand cmd)
            {
                var handler = new CalculatePayoutsForPeriodHandler(
                    db, new PayoutEngineFixture.FixedTenantContext(tenantId), new FixedUser(),
                    new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
                    NullLogger<CalculatePayoutsForPeriodHandler>.Instance);
                return (TResponse)(object)await handler.Handle(cmd, ct);
            }
            throw new NotSupportedException($"DirectSender: {request.GetType().Name} not wired.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
            => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> r, CancellationToken ct = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object r, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static CalculatePayRunHandler CalcRunHandler(ApplicationDbContext db, Guid tenantId) =>
        new(db, AlwaysAllowAuth.Instance, new PayoutEngineFixture.FixedTenantContext(tenantId),
            new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            new DirectSender(db, tenantId));

    private static ApprovePayRunHandler ApproveHandler(ApplicationDbContext db) =>
        new(db, AlwaysAllowAuth.Instance, new FixedUser(), new FakeClock(Now.UtcDateTime),
            new FakeGuidGenerator(), NoOpAuditService.Instance);

    private static MarkPayRunPaidHandler MarkPaidHandler(ApplicationDbContext db) =>
        new(db, AlwaysAllowAuth.Instance, new FixedUser(), new FakeClock(Now.UtcDateTime),
            new FakeGuidGenerator(), NoOpAuditService.Instance,
            new PayRunSettlementService(db, new FakeGuidGenerator()),
            NullLogger<MarkPayRunPaidHandler>.Instance);

    // ── Seeding ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One payee, one plan with the given cap, one transaction whose 10% credit becomes the payout.
    /// </summary>
    private async Task<(Guid TenantId, Guid PayeeId, Guid PlanId)> SeedPayeeWithCommissionAsync(
        decimal saleAmount, decimal? capPercent, string code = "EMP-CB")
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}@test.com",
                new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
            db.Payees.Add(payee);

            var plan = Plan.Create(tenantId, $"Plan {code}", "desc",
                DateRange.Of(PeriodStart, PeriodEnd), Eur, "test", planId, Now, Guid.NewGuid());
            plan.AddRule("Commission", 1,
                new Measurement
                {
                    Type = MeasurementType.Revenue,
                    SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum,
                },
                RateTable.Flat(0.10m));
            plan.SetClawbackPolicy(90, capPercent, "test", Now);
            db.CompensationPlans.Add(plan);

            db.PlanAssignments.Add(PlanAssignment.Create(
                tenantId, planId, payee.Id, PayeeReference.Snapshot(payee.Id, payee.FullName, code),
                DateRange.Of(PeriodStart, PeriodEnd), "test", Guid.NewGuid(), Now, Guid.NewGuid()));
            await db.SaveChangesAsync();

            await SeedCreditAsync(db, tenantId, payee.Id, planId, saleAmount);
            return (tenantId, payee.Id, planId);
        }
    }

    private static async Task SeedCreditAsync(
        ApplicationDbContext db, Guid tenantId, Guid payeeId, Guid planId, decimal saleAmount)
    {
        var plan = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId);
        var ruleId = plan.Rules.First().Id;
        var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
            RateTable.Flat(0.10m), Trigger.Always(), Now);

        var tx = CompensationTransaction.Ingest(
            tenantId, $"REF-{Guid.NewGuid():N}"[..16], payeeId, Money.Of(saleAmount, Eur),
            new DateOnly(2026, 2, 15), TransactionSource.Manual, "test",
            Guid.NewGuid(), Now, Guid.NewGuid());
        db.CompensationTransactions.Add(tx);

        var commission = Money.Of(saleAmount * 0.10m, Eur);
        db.Credits.Add(Credit.Allocate(
            tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
            Money.Of(saleAmount, Eur), commission, Percentage.FromPercent(100),
            CreditRole.Primary, "test", Guid.NewGuid(), Now, Guid.NewGuid()));
        tx.MarkCalculated(1, commission, "test", Now, Guid.NewGuid());
        await db.SaveChangesAsync();
    }

    /// <summary>Puts the payee in debt: a System clawback entry plus the balance it moves.</summary>
    private async Task SeedDebtAsync(Guid tenantId, Guid payeeId, decimal debt)
    {
        await using var db = fixture.CreateDbForTenant(tenantId);
        var entry = PayeeLedgerEntry.CreateSystemEntry(
            tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(debt, Eur),
            "Deal churned inside maturation.", LedgerSourceType.DealChurn,
            "system", Guid.NewGuid(), Now, Guid.NewGuid());
        var balance = PayeeBalance.Open(tenantId, payeeId, Eur, Guid.NewGuid(), Now);
        balance.Apply(entry, Now);
        db.PayeeLedgerEntries.Add(entry);
        db.PayeeBalances.Add(balance);
        await db.SaveChangesAsync();
    }

    /// <summary>Calculate → Approve → MarkPaid, each in its own context like the real request cycle.</summary>
    private async Task<Guid> RunToPaidAsync(Guid tenantId)
    {
        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
            runId = (await CalcRunHandler(db, tenantId)
                .Handle(new CalculatePayRunCommand(PeriodStart, PeriodEnd), CancellationToken.None)).Value!.PayRunId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
            (await ApproveHandler(db).Handle(new ApprovePayRunCommand(runId), CancellationToken.None))
                .IsSuccess.Should().BeTrue();

        await using (var db = fixture.CreateDbForTenant(tenantId))
            (await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runId), CancellationToken.None))
                .IsSuccess.Should().BeTrue();

        return runId;
    }

    // ══ The cap, the withholding and the carryover ═══════════════════════════

    [Fact]
    public async Task Debt_is_withheld_up_to_the_plan_cap_and_the_rest_carries_over()
    {
        // 10,000 sale → 1,000 commission. Cap 50% → at most 500 withheld against a 800 debt.
        var (tenantId, payeeId, _) = await SeedPayeeWithCommissionAsync(10_000m, capPercent: 50m);
        await SeedDebtAsync(tenantId, payeeId, 800m);

        var runId = await RunToPaidAsync(tenantId);

        await using var db = fixture.CreateDbForTenant(tenantId);

        var settlement = await db.PayRunSettlements.SingleAsync(s => s.PayRunId == runId);
        settlement.GrossCommission.Amount.Should().Be(1000m);
        settlement.ClawbackWithheld.Amount.Should().Be(500m);
        settlement.NetPaid.Amount.Should().Be(500m, "the payee never loses more than the cap allows");
        settlement.CarryoverRemaining.Amount.Should().Be(300m);

        var balance = await db.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId);
        balance.Balance.Amount.Should().Be(-300m, "only what was withheld reduces the debt");

        // The ledger closes: the sum of its entries equals the balance.
        var entries = await db.PayeeLedgerEntries.Where(e => e.PayeeId == payeeId).ToListAsync();
        entries.Sum(e => e.Amount.Amount).Should().Be(-300m);
        var applied = entries.Single(e => e.TransactionType == LedgerTransactionType.ClawbackAppliedCredit);
        applied.Amount.Amount.Should().Be(500m);
        applied.Origin.Should().Be(LedgerEntryOrigin.System);
        applied.SourcePayRunId.Should().Be(runId);
        settlement.LedgerEntryId.Should().Be(applied.Id);

        // The payout itself is untouched — the calculation stays immutable.
        var payout = await db.CompensationPayouts.SingleAsync(p => p.PayRunId == runId);
        payout.TotalCommission.Amount.Should().Be(1000m);
    }

    [Fact]
    public async Task A_payee_with_no_debt_is_paid_in_full_and_no_settlement_is_written()
    {
        var (tenantId, payeeId, _) = await SeedPayeeWithCommissionAsync(10_000m, capPercent: 50m);

        var runId = await RunToPaidAsync(tenantId);

        await using var db = fixture.CreateDbForTenant(tenantId);
        (await db.PayRunSettlements.CountAsync(s => s.PayRunId == runId)).Should().Be(0);
        (await db.PayeeLedgerEntries.CountAsync(e => e.PayeeId == payeeId)).Should().Be(0);
    }

    [Fact]
    public async Task A_debt_smaller_than_the_cap_is_collected_in_full_and_the_balance_reaches_zero()
    {
        var (tenantId, payeeId, _) = await SeedPayeeWithCommissionAsync(10_000m, capPercent: 50m);
        await SeedDebtAsync(tenantId, payeeId, 200m);

        var runId = await RunToPaidAsync(tenantId);

        await using var db = fixture.CreateDbForTenant(tenantId);
        var settlement = await db.PayRunSettlements.SingleAsync(s => s.PayRunId == runId);
        settlement.ClawbackWithheld.Amount.Should().Be(200m);
        settlement.NetPaid.Amount.Should().Be(800m);
        settlement.CarryoverRemaining.Amount.Should().Be(0m);
        (await db.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId)).Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task A_zero_cap_protects_the_whole_payment_and_the_debt_survives_untouched()
    {
        var (tenantId, payeeId, _) = await SeedPayeeWithCommissionAsync(10_000m, capPercent: 0m);
        await SeedDebtAsync(tenantId, payeeId, 800m);

        var runId = await RunToPaidAsync(tenantId);

        await using var db = fixture.CreateDbForTenant(tenantId);
        (await db.PayRunSettlements.CountAsync(s => s.PayRunId == runId)).Should().Be(0);
        (await db.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId)).Balance.Amount.Should().Be(-800m);
    }

    [Fact]
    public async Task The_balance_is_global_a_debt_is_collected_from_a_plan_that_did_not_create_it()
    {
        // THE reason the ledger is per payee and not per plan.
        var (tenantId, payeeId, _) = await SeedPayeeWithCommissionAsync(4_000m, capPercent: 50m, code: "EMP-A");

        // A second plan for the SAME payee. The debt was born under plan A; plan B also pays it down.
        var planB = Guid.NewGuid();
        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var plan = Plan.Create(tenantId, "Plan B", "desc",
                DateRange.Of(PeriodStart, PeriodEnd), Eur, "test", planB, Now, Guid.NewGuid());
            plan.AddRule("Commission", 1,
                new Measurement
                {
                    Type = MeasurementType.Revenue,
                    SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum,
                },
                RateTable.Flat(0.10m));
            plan.SetClawbackPolicy(90, 50m, "test", Now);
            db.CompensationPlans.Add(plan);
            db.PlanAssignments.Add(PlanAssignment.Create(
                tenantId, planB, payeeId, PayeeReference.Snapshot(payeeId, "Payee EMP-A", "EMP-A"),
                DateRange.Of(PeriodStart, PeriodEnd), "test", Guid.NewGuid(), Now, Guid.NewGuid()));
            await db.SaveChangesAsync();

            await SeedCreditAsync(db, tenantId, payeeId, planB, 10_000m);
        }

        // Plan A commission 400 (cap 50% → 200), plan B commission 1000 (cap 50% → 500). Debt 600.
        await SeedDebtAsync(tenantId, payeeId, 600m);

        var runId = await RunToPaidAsync(tenantId);

        await using var verifyDb = fixture.CreateDbForTenant(tenantId);
        var settlement = await verifyDb.PayRunSettlements.SingleAsync(s => s.PayRunId == runId);
        settlement.GrossCommission.Amount.Should().Be(1400m);
        settlement.ClawbackWithheld.Amount.Should().Be(600m, "both plans contribute to the same debt");
        settlement.NetPaid.Amount.Should().Be(800m);
        settlement.CarryoverRemaining.Amount.Should().Be(0m);
        (await verifyDb.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId)).Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task A_manual_forgiveness_reduces_the_debt_and_the_original_clawback_stays_visible()
    {
        var (tenantId, payeeId, _) = await SeedPayeeWithCommissionAsync(10_000m, capPercent: 50m);
        await SeedDebtAsync(tenantId, payeeId, 800m);

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var balance = await db.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId);
            var forgiveness = PayeeLedgerEntry.CreateManualAdjustment(
                tenantId, payeeId, LedgerTransactionType.ClawbackForgivenessCredit,
                Money.Of(600m, Eur), "Agreed with the rep — the churn was not their doing.",
                "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());
            balance.Apply(forgiveness, Now);
            db.PayeeLedgerEntries.Add(forgiveness);
            await db.SaveChangesAsync();
        }

        var runId = await RunToPaidAsync(tenantId);

        await using var db2 = fixture.CreateDbForTenant(tenantId);
        var settlement = await db2.PayRunSettlements.SingleAsync(s => s.PayRunId == runId);
        settlement.ClawbackWithheld.Amount.Should().Be(200m, "only 200 of the original 800 still stood");
        settlement.NetPaid.Amount.Should().Be(800m);

        var entries = await db2.PayeeLedgerEntries.Where(e => e.PayeeId == payeeId).ToListAsync();
        entries.Should().HaveCount(3);
        entries.Should().Contain(e => e.TransactionType == LedgerTransactionType.ClawbackDebit
                                   && e.Amount.Amount == -800m,
            "append-only: forgiving a debt never erases the entry that created it");
        entries.Sum(e => e.Amount.Amount).Should().Be(0m);
    }

    // ══ Concurrency: the RowVersion guard on the balance ═════════════════════

    [Fact]
    public async Task A_balance_written_by_two_contexts_at_once_fails_the_second_writer()
    {
        // Proves the OCC token is real in SQL, which is what makes a manual adjustment landing while a
        // pay run settles abort the run (all-or-nothing) instead of silently overwriting the balance
        // with a figure computed from stale data.
        var (tenantId, payeeId, _) = await SeedPayeeWithCommissionAsync(10_000m, capPercent: 50m);
        await SeedDebtAsync(tenantId, payeeId, 800m);

        await using var first = fixture.CreateDbForTenant(tenantId);
        await using var second = fixture.CreateDbForTenant(tenantId);

        var balanceInFirst = await first.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId);
        var balanceInSecond = await second.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId);

        var bonus = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.ManualBonusCredit, Money.Of(100m, Eur),
            "Adjustment that lands first.", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());
        balanceInSecond.Apply(bonus, Now);
        second.PayeeLedgerEntries.Add(bonus);
        await second.SaveChangesAsync();

        var late = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.ManualBonusCredit, Money.Of(50m, Eur),
            "Adjustment computed from a stale balance.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());
        balanceInFirst.Apply(late, Now);
        first.PayeeLedgerEntries.Add(late);

        var act = async () => await first.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verify = fixture.CreateDbForTenant(tenantId);
        (await verify.PayeeBalances.SingleAsync(b => b.PayeeId == payeeId))
            .Balance.Amount.Should().Be(-700m, "only the winning writer's adjustment applied");
    }
}
