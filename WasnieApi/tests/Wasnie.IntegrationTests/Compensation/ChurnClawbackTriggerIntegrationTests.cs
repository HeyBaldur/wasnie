#pragma warning disable CS8602

using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Ledger;
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
/// The churn trigger against REAL SQL: a paid commission whose deal died inside the maturation window
/// becomes a proportional debt, and that debt behaves correctly around a pay run.
///
/// These cannot be unit tests. The three things they assert are all properties of the database:
///   • the balance's OCC token is a SQL Server <c>rowversion</c> (EF InMemory neither generates nor
///     checks it, so a race assertion there would pass vacuously);
///   • whether a balance may actually go NEGATIVE is a property of the column and its constraints;
///   • the idempotency of the trigger is ultimately enforced by a unique filtered index.
/// </summary>
[Collection(PayoutEngineCollection.Name)]
public sealed class ChurnClawbackTriggerIntegrationTests(PayoutEngineFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";
    // The plan runs all year; the pay runs are quarterly. Two DISTINCT periods are needed because the
    // engine refuses to pay the same (payee, plan, exact period) twice — the anti-double-pay block — so a
    // "next pay run" is by definition the next PERIOD, not a second run over the same one.
    private static readonly DateOnly PlanStart = new(2026, 1, 1);
    private static readonly DateOnly PlanEnd = new(2026, 12, 31);
    private static readonly DateOnly PeriodStart = new(2026, 1, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);
    private static readonly DateOnly NextPeriodStart = new(2026, 4, 1);
    private static readonly DateOnly NextPeriodEnd = new(2026, 6, 30);
    private static readonly DateOnly DealClosedWonOn = new(2026, 2, 15);

    // ── Test doubles ──────────────────────────────────────────────────────────

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

    private static RegisterDealChurnClawbackHandler ChurnHandler(ApplicationDbContext db) =>
        new(db, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator());

    private static MarkPayRunPaidHandler MarkPaidHandler(ApplicationDbContext db) =>
        new(db, AlwaysAllowAuth.Instance, new FixedUser(), new FakeClock(Now.UtcDateTime),
            new FakeGuidGenerator(), NoOpAuditService.Instance,
            new PayRunSettlementService(db, new FakeGuidGenerator()),
            NullLogger<MarkPayRunPaidHandler>.Instance);

    // ── Seeding ───────────────────────────────────────────────────────────────

    private sealed record Seed(Guid TenantId, Guid PayeeId, Guid PlanId, Guid TxId);

    /// <summary>
    /// One payee, one plan (90-day maturation, cap 100% unless told otherwise), one CRM transaction whose
    /// 10% commission is calculated, approved and PAID — i.e. the money has actually left the company,
    /// which is the only state a clawback may act on.
    /// </summary>
    private async Task<Seed> SeedPaidCommissionAsync(
        decimal saleAmount, int? maturationDays = 90, decimal? capPercent = 100m, string code = "EMP-CHURN")
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        Guid payeeId, txId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
        {
            var payee = Payee.Create(tenantId, $"Payee {code}", code, $"{code}-{Guid.NewGuid():N}@test.com",
                new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
            db.Payees.Add(payee);
            payeeId = payee.Id;

            var plan = Plan.Create(tenantId, $"Plan {code}", "desc",
                DateRange.Of(PlanStart, PlanEnd), Eur, "test", planId, Now, Guid.NewGuid());
            plan.AddRule("Commission", 1,
                new Measurement
                {
                    Type = MeasurementType.Revenue,
                    SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum,
                },
                RateTable.Flat(0.10m));
            plan.SetClawbackPolicy(maturationDays, capPercent, "test", Now);
            db.CompensationPlans.Add(plan);

            db.PlanAssignments.Add(PlanAssignment.Create(
                tenantId, planId, payeeId, PayeeReference.Snapshot(payeeId, payee.FullName, code),
                DateRange.Of(PlanStart, PlanEnd), "test", Guid.NewGuid(), Now, Guid.NewGuid()));
            await db.SaveChangesAsync();

            var ruleId = (await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == planId))
                .Rules.First().Id;
            var snapshot = RuleSnapshot.Freeze(ruleId, planId, 1, "Commission",
                RateTable.Flat(0.10m), Trigger.Always(), Now);

            var tx = CompensationTransaction.Ingest(
                tenantId, $"HUBSPOT-{Guid.NewGuid():N}"[..16], payeeId, Money.Of(saleAmount, Eur),
                DealClosedWonOn, TransactionSource.CrmSync, "test",
                Guid.NewGuid(), Now, Guid.NewGuid(), externalId: "7001-1");
            db.CompensationTransactions.Add(tx);
            txId = tx.Id;

            var commission = Money.Of(saleAmount * 0.10m, Eur);
            db.Credits.Add(Credit.Allocate(
                tenantId, tx.Id, payeeId, planId, ruleId, snapshot,
                Money.Of(saleAmount, Eur), commission, Percentage.FromPercent(100),
                CreditRole.Primary, "test", Guid.NewGuid(), Now, Guid.NewGuid()));
            tx.MarkCalculated(1, commission, "test", Now, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        await RunToPaidAsync(tenantId); // the payment that makes a clawback possible at all

        return new Seed(tenantId, payeeId, planId, txId);
    }

    /// <summary>Calculate → Approve → MarkPaid, each in its own context like the real request cycle.</summary>
    private async Task<Guid> RunToPaidAsync(Guid tenantId, DateOnly? from = null, DateOnly? to = null)
    {
        var periodStart = from ?? PeriodStart;
        var periodEnd = to ?? PeriodEnd;
        Guid runId;
        await using (var db = fixture.CreateDbForTenant(tenantId))
            runId = (await new CalculatePayRunHandler(
                    db, AlwaysAllowAuth.Instance, new PayoutEngineFixture.FixedTenantContext(tenantId),
                    new FixedUser(), new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
                    new DirectSender(db, tenantId))
                .Handle(new CalculatePayRunCommand(periodStart, periodEnd), CancellationToken.None)).Value!.PayRunId;

        await using (var db = fixture.CreateDbForTenant(tenantId))
            (await new ApprovePayRunHandler(
                    db, AlwaysAllowAuth.Instance, new FixedUser(), new FakeClock(Now.UtcDateTime),
                    new FakeGuidGenerator(), NoOpAuditService.Instance)
                .Handle(new ApprovePayRunCommand(runId), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await using (var db = fixture.CreateDbForTenant(tenantId))
            (await MarkPaidHandler(db).Handle(new MarkPayRunPaidCommand(runId), CancellationToken.None))
                .IsSuccess.Should().BeTrue();

        return runId;
    }

    // ══ The debt itself ══════════════════════════════════════════════════════

    [Fact]
    public async Task A_deal_lost_inside_maturation_becomes_a_proportional_debit_in_the_ledger()
    {
        // 10,000 sale → 1,000 paid. Lost 30 days after closing, 90-day window → 1000 × 60 / 90 = 666.6667.
        var seed = await SeedPaidCommissionAsync(10_000m);
        var lostOn = DealClosedWonOn.AddDays(30);

        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            var result = await ChurnHandler(db).Handle(
                new RegisterDealChurnClawbackCommand(seed.TenantId, seed.TxId, lostOn, "7001"),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeDebited);
        }

        await using var verify = fixture.CreateDbForTenant(seed.TenantId);
        var entry = await verify.PayeeLedgerEntries
            .SingleAsync(e => e.SourceTransactionId == seed.TxId);

        entry.TransactionType.Should().Be(LedgerTransactionType.ClawbackDebit);
        entry.Origin.Should().Be(LedgerEntryOrigin.System);
        entry.SourceType.Should().Be(LedgerSourceType.DealChurn);
        entry.CreatedBy.Should().Be(RegisterDealChurnClawbackHandler.SystemActor);
        entry.Amount.Amount.Should().Be(-666.6667m);
        // Everything the figure was computed from survives the round trip to SQL.
        entry.SourceCommissionAmount.Should().Be(1000m);
        entry.DaysActive.Should().Be(30);
        entry.MaturationDays.Should().Be(90);
        entry.SourcePlanId.Should().Be(seed.PlanId);
        entry.EventDate.Should().Be(lostOn);

        (await verify.PayeeBalances.SingleAsync(b => b.PayeeId == seed.PayeeId))
            .Balance.Amount.Should().Be(-666.6667m);
    }

    [Fact]
    public async Task A_deal_lost_after_maturation_leaves_the_ledger_empty()
    {
        var seed = await SeedPaidCommissionAsync(10_000m);

        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            var result = await ChurnHandler(db).Handle(
                new RegisterDealChurnClawbackCommand(
                    seed.TenantId, seed.TxId, DealClosedWonOn.AddDays(200), "7001"),
                CancellationToken.None);
            result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeMatured);
        }

        await using var verify = fixture.CreateDbForTenant(seed.TenantId);
        (await verify.PayeeLedgerEntries.AnyAsync(e => e.PayeeId == seed.PayeeId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_plan_with_no_maturation_window_keeps_the_trigger_inert()
    {
        var seed = await SeedPaidCommissionAsync(10_000m, maturationDays: null, capPercent: null);

        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            var result = await ChurnHandler(db).Handle(
                new RegisterDealChurnClawbackCommand(
                    seed.TenantId, seed.TxId, DealClosedWonOn.AddDays(10), "7001"),
                CancellationToken.None);
            result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeNoPolicy);
        }

        await using var verify = fixture.CreateDbForTenant(seed.TenantId);
        (await verify.PayeeLedgerEntries.AnyAsync(e => e.PayeeId == seed.PayeeId)).Should().BeFalse();
    }

    // ══ Blindaje 1 — the event date never becomes the accounting date ════════

    [Fact]
    public async Task A_loss_dated_inside_an_already_paid_period_is_booked_in_the_open_one()
    {
        // The scenario the separation exists for: the pay run for Jan–Mar is closed, reconciled and PAID.
        // Someone then records in the CRM that the deal was lost on 1 March. Booking the debit into March
        // would alter a balance that has already been paid out; booking it now does not.
        var seed = await SeedPaidCommissionAsync(10_000m);
        var lostInsideThePaidPeriod = new DateOnly(2026, 3, 1);

        Guid paidRunId;
        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
            paidRunId = await db.PayRuns.Select(r => r.Id).SingleAsync();

        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            await ChurnHandler(db).Handle(
                new RegisterDealChurnClawbackCommand(seed.TenantId, seed.TxId, lostInsideThePaidPeriod, "7001"),
                CancellationToken.None);
        }

        await using var verify = fixture.CreateDbForTenant(seed.TenantId);
        var entry = await verify.PayeeLedgerEntries.SingleAsync(e => e.SourceTransactionId == seed.TxId);

        // The two dates are different, and it is the LATER one that books the money.
        entry.EventDate.Should().Be(lostInsideThePaidPeriod);
        entry.CreatedAt.Should().Be(Now);
        entry.CreatedAt.UtcDateTime.Date.Should().BeAfter(
            lostInsideThePaidPeriod.ToDateTime(TimeOnly.MinValue),
            "a new debit is never injected into a period that is already closed and paid");

        // And the closed run is untouched: no settlement was retro-fitted into it.
        (await verify.PayRunSettlements.AnyAsync(s => s.PayRunId == paidRunId)).Should().BeFalse();
        (await verify.PayeeLedgerEntries.AnyAsync(e => e.SourcePayRunId == paidRunId)).Should().BeFalse();
    }

    // ══ Blindaje 2 — the race against a pay run being closed ═════════════════

    [Fact]
    public async Task A_debit_racing_a_pay_run_settlement_is_neither_lost_nor_double_counted()
    {
        // Two writers on ONE balance row: the churn trigger (a CRM webhook) and the pay run being marked
        // paid (finance). The trigger reads the balance, the pay run settles and moves it, and only THEN
        // does the trigger write. Without the rowversion the trigger's UPDATE would silently overwrite the
        // settlement; with it, the trigger's write fails, re-reads and re-applies on top of the winner.
        var seed = await SeedPaidCommissionAsync(10_000m, capPercent: 100m, code: "EMP-RACE");

        // An existing debt of 100 so the second pay run has something to settle against.
        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            var opening = PayeeLedgerEntry.CreateSystemEntry(
                seed.TenantId, seed.PayeeId, LedgerTransactionType.ClawbackDebit, Money.Of(100m, Eur),
                "Pre-existing debt.", LedgerSourceType.OriginError, "system",
                Guid.NewGuid(), Now, Guid.NewGuid());
            var balance = PayeeBalance.Open(seed.TenantId, seed.PayeeId, Eur, Guid.NewGuid(), Now);
            balance.Apply(opening, Now);
            db.PayeeLedgerEntries.Add(opening);
            db.PayeeBalances.Add(balance);
            await db.SaveChangesAsync();
        }

        // A second, unpaid commission for the same payee so a second pay run has something to pay.
        await SeedSecondCommissionAsync(seed, 5_000m);

        await using var churnDb = fixture.CreateDbForTenant(seed.TenantId);

        // The trigger's context loads the balance FIRST — this is its stale read.
        var staleBalance = await churnDb.PayeeBalances.SingleAsync(b => b.PayeeId == seed.PayeeId);
        staleBalance.Balance.Amount.Should().Be(-100m);

        // …while, in another context, finance closes a run that settles that same 100.
        var secondRunId = await RunToPaidAsync(seed.TenantId, NextPeriodStart, NextPeriodEnd);

        // Now the trigger writes its 666.6667 debit on top of a balance that moved under it.
        var result = await ChurnHandler(churnDb).Handle(
            new RegisterDealChurnClawbackCommand(
                seed.TenantId, seed.TxId, DealClosedWonOn.AddDays(30), "7001"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the retry resolves the conflict instead of dropping the debt");

        await using var verify = fixture.CreateDbForTenant(seed.TenantId);
        var entries = await verify.PayeeLedgerEntries
            .Where(e => e.PayeeId == seed.PayeeId).ToListAsync();

        // Exactly one churn debit: not lost, not written twice.
        entries.Count(e => e.SourceType == LedgerSourceType.DealChurn).Should().Be(1);
        // The settlement of the OTHER writer survived too.
        entries.Should().Contain(e => e.SourcePayRunId == secondRunId
                                   && e.TransactionType == LedgerTransactionType.ClawbackAppliedCredit);

        // The invariant that proves nothing was lost or counted twice: balance == sum of entries.
        var balanceAfter = await verify.PayeeBalances.SingleAsync(b => b.PayeeId == seed.PayeeId);
        balanceAfter.Balance.Amount.Should().Be(entries.Sum(e => e.Amount.Amount));
        // −100 (debt) + 100 (settled by the run) − 666.6667 (churn) = −666.6667
        balanceAfter.Balance.Amount.Should().Be(-666.6667m);
    }

    // ══ Blindaje 3 — the balance may go negative and carries ═════════════════

    [Fact]
    public async Task A_clawback_larger_than_the_balance_leaves_it_negative_and_the_debt_carries()
    {
        // Decision (Rodolfo, 2026-07-28): no floor at zero. A rep who earned 100 and owes 400 has a
        // balance of −400 + 100; the ledger records what is true rather than what is comfortable, and a
        // floor would reward timing a churn against an empty account.
        var seed = await SeedPaidCommissionAsync(10_000m, code: "EMP-NEG");

        // Give the payee a small positive balance first: 100 in their favour.
        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            var bonus = PayeeLedgerEntry.CreateManualAdjustment(
                seed.TenantId, seed.PayeeId, LedgerTransactionType.ManualBonusCredit, Money.Of(100m, Eur),
                "Goodwill bonus.", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());
            var balance = PayeeBalance.Open(seed.TenantId, seed.PayeeId, Eur, Guid.NewGuid(), Now);
            balance.Apply(bonus, Now);
            db.PayeeLedgerEntries.Add(bonus);
            db.PayeeBalances.Add(balance);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            // Lost the day after it closed → 1000 × 89 / 90 = 988.8889, far more than the 100 available.
            await ChurnHandler(db).Handle(
                new RegisterDealChurnClawbackCommand(
                    seed.TenantId, seed.TxId, DealClosedWonOn.AddDays(1), "7001"),
                CancellationToken.None);
        }

        await using var verify = fixture.CreateDbForTenant(seed.TenantId);
        var balanceAfter = await verify.PayeeBalances.SingleAsync(b => b.PayeeId == seed.PayeeId);

        // SQL accepts it: there is no check constraint, no domain invariant and no EF validation that
        // forbids a negative balance. This test is the documentation of that (Blindaje 3, Opción 1).
        balanceAfter.Balance.Amount.Should().Be(-888.8889m);
        balanceAfter.OutstandingDebt().Amount.Should().Be(888.8889m);

        var entries = await verify.PayeeLedgerEntries.Where(e => e.PayeeId == seed.PayeeId).ToListAsync();
        entries.Sum(e => e.Amount.Amount).Should().Be(-888.8889m, "the ledger still closes");
    }

    [Fact]
    public async Task The_carried_debt_is_netted_against_the_next_pay_run()
    {
        // End to end: churned deal → debt → the NEXT run collects it. This is what makes the trigger
        // "alive" rather than a row nobody acts on.
        var seed = await SeedPaidCommissionAsync(10_000m, code: "EMP-NET");

        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            await ChurnHandler(db).Handle(
                new RegisterDealChurnClawbackCommand(
                    seed.TenantId, seed.TxId, DealClosedWonOn.AddDays(30), "7001"),
                CancellationToken.None);
        }

        // A fresh, unpaid commission of 500 for the same payee, then a new run.
        await SeedSecondCommissionAsync(seed, 5_000m);
        var nextRunId = await RunToPaidAsync(seed.TenantId, NextPeriodStart, NextPeriodEnd);

        await using var verify = fixture.CreateDbForTenant(seed.TenantId);
        var settlement = await verify.PayRunSettlements.SingleAsync(s => s.PayRunId == nextRunId);

        settlement.GrossCommission.Amount.Should().Be(500m);
        settlement.ClawbackWithheld.Amount.Should().Be(500m, "the whole payment goes against the debt");
        settlement.NetPaid.Amount.Should().Be(0m);
        settlement.CarryoverRemaining.Amount.Should().Be(166.6667m);

        (await verify.PayeeBalances.SingleAsync(b => b.PayeeId == seed.PayeeId))
            .Balance.Amount.Should().Be(-166.6667m, "what could not be collected keeps carrying");
    }

    // ══ Idempotency, at the level where it is actually enforced ══════════════

    [Fact]
    public async Task The_database_refuses_a_second_churn_debit_for_the_same_transaction_and_plan()
    {
        // The handler checks before writing, but a read-then-write check cannot survive two syncs racing.
        // The unique filtered index can — this proves it exists and bites.
        var seed = await SeedPaidCommissionAsync(10_000m, code: "EMP-IDEM");

        await using (var db = fixture.CreateDbForTenant(seed.TenantId))
        {
            await ChurnHandler(db).Handle(
                new RegisterDealChurnClawbackCommand(
                    seed.TenantId, seed.TxId, DealClosedWonOn.AddDays(30), "7001"),
                CancellationToken.None);
        }

        await using var db2 = fixture.CreateDbForTenant(seed.TenantId);
        var duplicate = PayeeLedgerEntry.CreateSystemEntry(
            seed.TenantId, seed.PayeeId, LedgerTransactionType.ClawbackDebit, Money.Of(1m, Eur),
            "A duplicate the guard did not see.", LedgerSourceType.DealChurn, "system",
            Guid.NewGuid(), Now, Guid.NewGuid(),
            sourceTransactionId: seed.TxId, sourcePlanId: seed.PlanId,
            eventDate: DealClosedWonOn.AddDays(30));
        db2.PayeeLedgerEntries.Add(duplicate);

        var act = async () => await db2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>A second, still-unpaid commission for the same payee/plan, so a further pay run has
    /// something to pay (and therefore something to withhold from).</summary>
    private async Task SeedSecondCommissionAsync(Seed seed, decimal saleAmount)
    {
        await using var db = fixture.CreateDbForTenant(seed.TenantId);
        var plan = await db.CompensationPlans.Include(p => p.Rules).FirstAsync(p => p.Id == seed.PlanId);
        var ruleId = plan.Rules.First().Id;
        var snapshot = RuleSnapshot.Freeze(ruleId, seed.PlanId, 1, "Commission",
            RateTable.Flat(0.10m), Trigger.Always(), Now);

        var tx = CompensationTransaction.Ingest(
            seed.TenantId, $"REF2-{Guid.NewGuid():N}"[..16], seed.PayeeId, Money.Of(saleAmount, Eur),
            new DateOnly(2026, 4, 10), TransactionSource.Manual, "test",
            Guid.NewGuid(), Now, Guid.NewGuid());
        db.CompensationTransactions.Add(tx);

        var commission = Money.Of(saleAmount * 0.10m, Eur);
        db.Credits.Add(Credit.Allocate(
            seed.TenantId, tx.Id, seed.PayeeId, seed.PlanId, ruleId, snapshot,
            Money.Of(saleAmount, Eur), commission, Percentage.FromPercent(100),
            CreditRole.Primary, "test", Guid.NewGuid(), Now, Guid.NewGuid()));
        tx.MarkCalculated(1, commission, "test", Now, Guid.NewGuid());
        await db.SaveChangesAsync();
    }
}
