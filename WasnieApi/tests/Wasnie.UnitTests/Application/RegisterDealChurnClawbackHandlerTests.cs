using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Ledger;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Ledger;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The churn trigger: a lost deal whose commission was already PAID becomes a proportional debt.
///
/// These cover the arithmetic at its edges, the opt-in switch, the append-only stamps and — the one that
/// matters most for the audit — that the CRM's event date NEVER becomes the booking date.
/// The OCC race and the real negative balance live in the integration suite, because EF InMemory neither
/// generates rowversions nor enforces the check constraints a real balance column would have.
/// </summary>
public sealed class RegisterDealChurnClawbackHandlerTests
{
    private static readonly DateTimeOffset BookedNow = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ClosedWonOn = new(2026, 1, 1);
    private const string Eur = "EUR";

    private sealed record Harness(
        ApplicationDbContext Db, RegisterDealChurnClawbackHandler Handler, Guid TenantId, Guid PayeeId,
        Guid PlanId, Guid TxId);

    /// <summary>Seeds a PAID CrmSync transaction: one credit of <paramref name="commission"/>, consumed by
    /// a payout, under a plan whose maturation window is <paramref name="maturationDays"/> (null = opt-out).</summary>
    private static Harness Build(string dbName, decimal commission = 900m, int? maturationDays = 90)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        var payeeId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        var plan = Plan.Create(
            tenantId, "Churn plan", "", DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            Eur, "test", Guid.NewGuid(), BookedNow, Guid.NewGuid());
        if (maturationDays is not null)
            plan.SetClawbackPolicy(maturationDays, null, "test", BookedNow);

        var tx = CompensationTransaction.Ingest(
            tenantId: tenantId, referenceNumber: "HUBSPOT-5000-1", payeeId: payeeId,
            amount: Money.Of(9000m, Eur), transactionDate: ClosedWonOn,
            source: TransactionSource.CrmSync, ingestedBy: "sync", id: Guid.NewGuid(), now: BookedNow,
            eventId: Guid.NewGuid(), externalId: "5000-1");

        var snapshot = RuleSnapshot.Freeze(
            ruleId, plan.Id, 1, "Commission", RateTable.Flat(0.10m), Trigger.Always(), BookedNow);
        var credit = Credit.Allocate(
            tenantId, tx.Id, payeeId, plan.Id, ruleId, snapshot, Money.Of(9000m, Eur),
            Money.Of(commission, Eur), Percentage.FromPercent(100), CreditRole.Primary,
            "sync", Guid.NewGuid(), BookedNow, Guid.NewGuid());

        tx.MarkCalculated(1, Money.Of(commission, Eur), "sync", BookedNow, Guid.NewGuid());
        tx.MarkPaid("sync", BookedNow, Guid.NewGuid());
        credit.Consume(Guid.NewGuid(), BookedNow, Guid.NewGuid());

        db.CompensationPlans.Add(plan);
        db.CompensationTransactions.Add(tx);
        db.Credits.Add(credit);
        db.SaveChanges();

        var handler = new RegisterDealChurnClawbackHandler(
            db, new FakeClock(BookedNow.UtcDateTime), new FakeGuidGenerator());

        return new Harness(db, handler, tenantId, payeeId, plan.Id, tx.Id);
    }

    private static RegisterDealChurnClawbackCommand Command(Harness h, DateOnly lostOn) =>
        new(h.TenantId, h.TxId, lostOn, "5000");

    // ── The formula, at its edges ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lost_the_same_day_it_was_won_claws_back_the_whole_commission()
    {
        var h = Build(nameof(Lost_the_same_day_it_was_won_claws_back_the_whole_commission));

        var result = await h.Handler.Handle(Command(h, ClosedWonOn), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeDebited);
        // Signed, like every ledger figure: a debit is negative all the way out to the DTO.
        result.Value.Entries.Single().Amount.Should().Be(-900m); // 900 × (90 − 0) / 90
        result.Value.Entries.Single().DaysActive.Should().Be(0);
    }

    [Fact]
    public async Task Lost_halfway_through_maturation_claws_back_half()
    {
        var h = Build(nameof(Lost_halfway_through_maturation_claws_back_half));

        var result = await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(45)), default);

        // 900 × (90 − 45) / 90 = 450.00 exactly — one multiply, one divide, no rounding drift.
        result.Value!.Entries.Single().Amount.Should().Be(-450m);
    }

    [Fact]
    public async Task Lost_after_maturation_claws_back_nothing_and_writes_no_entry()
    {
        var h = Build(nameof(Lost_after_maturation_claws_back_nothing_and_writes_no_entry));

        var result = await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(120)), default);

        result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeMatured);
        result.Value.Entries.Should().BeEmpty();
        // The floor is not "an entry of zero": a matured deal leaves the ledger untouched.
        (await h.Db.PayeeLedgerEntries.AnyAsync()).Should().BeFalse();
        (await h.Db.PayeeBalances.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Lost_exactly_on_the_maturation_day_claws_back_nothing()
    {
        var h = Build(nameof(Lost_exactly_on_the_maturation_day_claws_back_nothing));

        var result = await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(90)), default);

        result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeMatured);
    }

    // ── Opt-in ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_plan_without_a_maturation_window_generates_no_debt()
    {
        var h = Build(nameof(A_plan_without_a_maturation_window_generates_no_debt), maturationDays: null);

        var result = await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(10)), default);

        result.IsSuccess.Should().BeTrue(); // inert, NOT an error
        result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeNoPolicy);
        (await h.Db.PayeeLedgerEntries.AnyAsync()).Should().BeFalse();
    }

    // ── Blindaje 1: event time is not booking time ────────────────────────────────────────────────

    [Fact]
    public async Task An_event_date_in_a_closed_period_is_booked_in_the_open_one()
    {
        // The CRM says the deal died in March. March is closed, reconciled and paid. The debt is real, so
        // it is booked TODAY and the March date travels as evidence — never as the accounting date.
        var h = Build(nameof(An_event_date_in_a_closed_period_is_booked_in_the_open_one));
        var lostInMarch = new DateOnly(2026, 3, 15);

        var result = await h.Handler.Handle(Command(h, lostInMarch), default);

        var entry = await h.Db.PayeeLedgerEntries.SingleAsync();
        entry.CreatedAt.Should().Be(BookedNow);                       // booked in the OPEN period
        entry.CreatedAt.UtcDateTime.Month.Should().Be(7);
        entry.EventDate.Should().Be(lostInMarch);                     // the March fact, preserved
        entry.EventDate.Should().NotBe(DateOnly.FromDateTime(entry.CreatedAt.UtcDateTime));

        // …and the arithmetic used March, not today: 900 × (90 − 73) / 90.
        entry.DaysActive.Should().Be(73);
        result.Value!.Entries.Single().EventDate.Should().Be(lostInMarch);
    }

    // ── The entry's identity and provenance ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_entry_is_a_system_clawback_debit_with_every_input_it_was_computed_from()
    {
        var h = Build(nameof(The_entry_is_a_system_clawback_debit_with_every_input_it_was_computed_from));

        await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(30)), default);

        var entry = await h.Db.PayeeLedgerEntries.SingleAsync();
        entry.Origin.Should().Be(LedgerEntryOrigin.System);
        entry.TransactionType.Should().Be(LedgerTransactionType.ClawbackDebit);
        entry.SourceType.Should().Be(LedgerSourceType.DealChurn);
        entry.CreatedBy.Should().Be(RegisterDealChurnClawbackHandler.SystemActor);
        entry.Amount.Amount.Should().Be(-600m); // debit → stored negative, sign derived from the type
        entry.SourceCommissionAmount.Should().Be(900m);
        entry.DaysActive.Should().Be(30);
        entry.MaturationDays.Should().Be(90);
        entry.SourcePlanId.Should().Be(h.PlanId);
        entry.SourceTransactionId.Should().Be(h.TxId);
        entry.SourceExternalDealId.Should().Be("5000");
    }

    [Fact]
    public async Task The_debit_moves_the_payee_balance_and_leaves_the_transaction_and_credits_untouched()
    {
        var h = Build(nameof(The_debit_moves_the_payee_balance_and_leaves_the_transaction_and_credits_untouched));

        await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(30)), default);

        var balance = await h.Db.PayeeBalances.SingleAsync();
        balance.Balance.Amount.Should().Be(-600m);
        balance.OutstandingDebt().Amount.Should().Be(600m);

        // The payment happened. Nothing about it is rewritten.
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.Status.Should().Be(CompensationTransactionStatus.Paid);
        var credit = await h.Db.Credits.SingleAsync();
        credit.SupersededAt.Should().BeNull();
        credit.ConsumedAt.Should().NotBeNull();
    }

    // ── Idempotency and guards ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Running_the_trigger_twice_posts_one_debit()
    {
        // The reverse reconciler re-sees a lost deal on every sync, forever.
        var h = Build(nameof(Running_the_trigger_twice_posts_one_debit));

        await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(30)), default);
        var second = await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(30)), default);

        second.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeAlreadyPosted);
        (await h.Db.PayeeLedgerEntries.CountAsync()).Should().Be(1);
        (await h.Db.PayeeBalances.SingleAsync()).Balance.Amount.Should().Be(-600m);
    }

    [Fact]
    public async Task A_calculated_commission_is_refused_it_belongs_to_the_revert()
    {
        var h = Build(nameof(A_calculated_commission_is_refused_it_belongs_to_the_revert));
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        // Force the tx back to Calculated to model the unpaid case.
        h.Db.Entry(tx).Property(nameof(CompensationTransaction.Status))
            .CurrentValue = CompensationTransactionStatus.Calculated;
        await h.Db.SaveChangesAsync();

        var result = await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(30)), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("PAID");
        (await h.Db.PayeeLedgerEntries.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_commission_that_was_never_actually_paid_out_generates_no_debt()
    {
        // Paid transaction, but its credit was never consumed by a payout: no money left the company.
        var h = Build(nameof(A_commission_that_was_never_actually_paid_out_generates_no_debt));
        var credit = await h.Db.Credits.SingleAsync();
        h.Db.Entry(credit).Property(nameof(Credit.ConsumedAt)).CurrentValue = null;
        await h.Db.SaveChangesAsync();

        var result = await h.Handler.Handle(Command(h, ClosedWonOn.AddDays(30)), default);

        result.Value!.Outcome.Should().Be(DealChurnClawbackDto.OutcomeNothingPaid);
        (await h.Db.PayeeLedgerEntries.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_transaction_is_refused()
    {
        var h = Build(nameof(An_unknown_transaction_is_refused));

        var result = await h.Handler.Handle(
            new RegisterDealChurnClawbackCommand(h.TenantId, Guid.NewGuid(), ClosedWonOn, "5000"), default);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Another_tenants_transaction_is_invisible_to_this_trigger()
    {
        var h = Build(nameof(Another_tenants_transaction_is_invisible_to_this_trigger));

        var result = await h.Handler.Handle(
            new RegisterDealChurnClawbackCommand(Guid.NewGuid(), h.TxId, ClosedWonOn, "5000"), default);

        result.IsSuccess.Should().BeFalse();
        (await h.Db.PayeeLedgerEntries.AnyAsync()).Should().BeFalse();
    }
}
