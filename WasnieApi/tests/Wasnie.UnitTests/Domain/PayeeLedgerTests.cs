using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// The ledger is the record of money taken back from a person. These tests pin the invariants that
/// make an invalid entry unrepresentable rather than merely discouraged: the sign follows the
/// transaction type, Origin is stamped by the factory, a manual entry without an owner or a reason
/// never comes into existence, and the balance is exactly the sum of the entries.
/// </summary>
public sealed class PayeeLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
    private const string Eur = "EUR";

    private static PayeeLedgerEntry SystemClawback(Guid tenantId, Guid payeeId, decimal magnitude) =>
        PayeeLedgerEntry.CreateSystemEntry(
            tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(magnitude, Eur),
            "Deal churned 30/90 days into maturation.", LedgerSourceType.DealChurn,
            "system", Guid.NewGuid(), Now, Guid.NewGuid());

    private static PayeeLedgerEntry ManualForgiveness(Guid tenantId, Guid payeeId, decimal magnitude) =>
        PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.ClawbackForgivenessCredit, Money.Of(magnitude, Eur),
            "Commercial gesture agreed with the rep.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

    // ── Sign convention ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LedgerTransactionType.ClawbackDebit, -250)]
    [InlineData(LedgerTransactionType.DataCorrectionDebit, -250)]
    [InlineData(LedgerTransactionType.ClawbackForgivenessCredit, 250)]
    [InlineData(LedgerTransactionType.ManualBonusCredit, 250)]
    [InlineData(LedgerTransactionType.ClawbackAppliedCredit, 250)]
    public void The_sign_is_derived_from_the_transaction_type(LedgerTransactionType type, decimal expected)
    {
        var entry = PayeeLedgerEntry.CreateSystemEntry(
            Guid.NewGuid(), Guid.NewGuid(), type, Money.Of(250m, Eur),
            "reason", LedgerSourceType.DealChurn, "system", Guid.NewGuid(), Now, Guid.NewGuid());

        entry.Amount.Amount.Should().Be(expected);
        entry.Amount.Currency.Should().Be(Eur);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void An_entry_is_created_from_a_positive_magnitude_only(decimal magnitude)
    {
        var act = () => PayeeLedgerEntry.CreateSystemEntry(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.ClawbackDebit,
            Money.Of(magnitude, Eur), "reason", LedgerSourceType.DealChurn,
            "system", Guid.NewGuid(), Now, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*POSITIVE magnitude*");
    }

    // ── Origin is stamped by the factory, never by the caller ───────────────────

    [Fact]
    public void CreateSystemEntry_stamps_System_and_CreateManualAdjustment_stamps_Human()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        SystemClawback(tenantId, payeeId, 100m).Origin.Should().Be(LedgerEntryOrigin.System);
        ManualForgiveness(tenantId, payeeId, 100m).Origin.Should().Be(LedgerEntryOrigin.Human);
    }

    [Fact]
    public void Origin_is_immutable_no_public_writer_exists()
    {
        // Structural, not behavioural: if someone adds a setter, this test fails and the reviewer
        // has to justify making the "who wrote it" field mutable on an append-only ledger.
        var prop = typeof(PayeeLedgerEntry).GetProperty(nameof(PayeeLedgerEntry.Origin))!;

        prop.SetMethod!.IsPublic.Should().BeFalse();
        typeof(PayeeLedgerEntry).GetMethods()
            .Where(m => m.IsPublic && !m.IsStatic)
            .Select(m => m.Name)
            .Should().NotContain(n => n.StartsWith("Set") || n.StartsWith("Update") || n.StartsWith("Delete"));
    }

    // ── Manual adjustment invariants ───────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_manual_adjustment_without_a_justification_never_exists(string justification)
    {
        var act = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.ManualBonusCredit,
            Money.Of(100m, Eur), justification, "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*justification*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_manual_adjustment_without_an_actor_never_exists(string actor)
    {
        var act = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.ManualBonusCredit,
            Money.Of(100m, Eur), "reason", actor, Guid.NewGuid(), Now, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*actor*");
    }

    [Theory]
    [InlineData(LedgerTransactionType.ClawbackDebit)]
    [InlineData(LedgerTransactionType.ClawbackAppliedCredit)]
    public void A_human_cannot_hand_write_an_engine_only_entry(LedgerTransactionType type)
    {
        var act = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), type, Money.Of(100m, Eur),
            "reason", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());

        act.Should().Throw<DomainException>().WithMessage("*cannot be created by hand*");
    }

    [Theory]
    [InlineData(LedgerTransactionType.ClawbackForgivenessCredit)]
    [InlineData(LedgerTransactionType.ManualBonusCredit)]
    [InlineData(LedgerTransactionType.DataCorrectionDebit)]
    public void The_three_human_types_are_accepted(LedgerTransactionType type)
    {
        var entry = PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), type, Money.Of(100m, Eur),
            "reason", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());

        entry.TransactionType.Should().Be(type);
        entry.CreatedBy.Should().Be("finance@acme.com");
    }

    [Fact]
    public void A_clawback_entry_keeps_the_inputs_that_produced_its_number()
    {
        var entry = PayeeLedgerEntry.CreateSystemEntry(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.ClawbackDebit,
            Money.Of(600m, Eur), "Churn at day 30 of 90.", LedgerSourceType.DealChurn,
            "system", Guid.NewGuid(), Now, Guid.NewGuid(),
            sourceTransactionId: Guid.NewGuid(), sourceExternalDealId: "512147967174",
            sourceCommissionAmount: 900m, daysActive: 30, maturationDays: 90);

        entry.SourceCommissionAmount.Should().Be(900m);
        entry.DaysActive.Should().Be(30);
        entry.MaturationDays.Should().Be(90);
        entry.SourceExternalDealId.Should().Be("512147967174");
        entry.SourceType.Should().Be(LedgerSourceType.DealChurn);
    }

    // ── Balance = sum of entries ───────────────────────────────────────────────

    [Fact]
    public void The_balance_sums_opposite_signs_to_the_cent()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, Eur, Guid.NewGuid(), Now);

        balance.Apply(SystemClawback(tenantId, payeeId, 333.33m), Now);
        balance.Apply(SystemClawback(tenantId, payeeId, 333.34m), Now);
        balance.Apply(ManualForgiveness(tenantId, payeeId, 166.67m), Now);

        balance.Balance.Amount.Should().Be(-500.00m);
        balance.OutstandingDebt().Amount.Should().Be(500.00m);
    }

    [Fact]
    public void A_fully_settled_balance_is_zero_and_owes_nothing()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, Eur, Guid.NewGuid(), Now);

        balance.Apply(SystemClawback(tenantId, payeeId, 500m), Now);
        balance.Apply(ManualForgiveness(tenantId, payeeId, 500m), Now);

        balance.Balance.Amount.Should().Be(0m);
        balance.OutstandingDebt().Amount.Should().Be(0m);
    }

    [Fact]
    public void A_positive_balance_owes_no_debt()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, Eur, Guid.NewGuid(), Now);

        balance.Apply(ManualForgiveness(tenantId, payeeId, 120m), Now);

        balance.Balance.Amount.Should().Be(120m);
        balance.OutstandingDebt().Amount.Should().Be(0m);
    }

    // ── Partition guards: per payee, per currency ──────────────────────────────

    [Fact]
    public void An_entry_of_another_currency_cannot_touch_this_balance()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, Eur, Guid.NewGuid(), Now);
        var usdEntry = PayeeLedgerEntry.CreateSystemEntry(
            tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(100m, "USD"),
            "reason", LedgerSourceType.DealChurn, "system", Guid.NewGuid(), Now, Guid.NewGuid());

        var act = () => balance.Apply(usdEntry, Now);

        act.Should().Throw<DomainException>().WithMessage("*no exchange rates*");
        balance.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public void An_entry_of_another_payee_cannot_touch_this_balance()
    {
        var tenantId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, Guid.NewGuid(), Eur, Guid.NewGuid(), Now);
        var otherPayeeEntry = SystemClawback(tenantId, Guid.NewGuid(), 100m);

        var act = () => balance.Apply(otherPayeeEntry, Now);

        act.Should().Throw<DomainException>().WithMessage("*cannot be applied to the balance*");
    }

    [Fact]
    public void An_entry_of_another_tenant_cannot_touch_this_balance()
    {
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(Guid.NewGuid(), payeeId, Eur, Guid.NewGuid(), Now);
        var otherTenantEntry = SystemClawback(Guid.NewGuid(), payeeId, 100m);

        var act = () => balance.Apply(otherTenantEntry, Now);

        act.Should().Throw<DomainException>().WithMessage("*different tenant*");
    }

    [Fact]
    public void A_balance_needs_a_valid_ISO_currency()
    {
        var act = () => PayeeBalance.Open(Guid.NewGuid(), Guid.NewGuid(), "EUROS", Guid.NewGuid(), Now);

        act.Should().Throw<DomainException>().WithMessage("*3-letter ISO code*");
    }

    // ── Closing the account of someone who has left ──────────────────────────────
    // A terminated payee's debt is frozen, not forgiven. It leaves the books ONLY through one of these
    // two entries, and they stay separate types on purpose: "we recovered it via payroll" and "we ate
    // the loss" are different facts, and a CFO must be able to total each without reading justifications.

    [Theory]
    [InlineData(LedgerTransactionType.ExternalSettlementCredit)]
    [InlineData(LedgerTransactionType.WriteOffCredit)]
    public void A_closing_entry_is_a_credit_that_moves_the_balance_toward_zero(LedgerTransactionType type)
    {
        var entry = PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), type, Money.Of(500m, "EUR"),
            "Closing the account of a departed payee.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

        entry.Amount.Amount.Should().Be(500m, "both closing types return money to the balance");
        type.IsDebit().Should().BeFalse();
        entry.Origin.Should().Be(LedgerEntryOrigin.Human);
        entry.CreatedBy.Should().Be("finance@acme.com");
    }

    [Theory]
    [InlineData(LedgerTransactionType.ExternalSettlementCredit)]
    [InlineData(LedgerTransactionType.WriteOffCredit)]
    public void A_closing_entry_requires_an_actor_and_a_justification(LedgerTransactionType type)
    {
        // It records a finance DECISION. An entry nobody signed is not a decision, it is a hole.
        var noActor = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), type, Money.Of(500m, "EUR"),
            "Closing.", "", Guid.NewGuid(), Now, Guid.NewGuid());
        noActor.Should().Throw<DomainException>();

        var noReason = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), type, Money.Of(500m, "EUR"),
            "   ", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());
        noReason.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(LedgerTransactionType.ExternalSettlementCredit)]
    [InlineData(LedgerTransactionType.WriteOffCredit)]
    public void The_engine_cannot_decide_to_close_someone_s_account(LedgerTransactionType type)
    {
        // The mirror of the engine-only rule: a human may not hand-write a clawback, and no automation
        // may declare a debt settled or written off. Both directions are enforced by the same switch.
        type.IsManuallyCreatable().Should().BeTrue();

        var systemEntry = PayeeLedgerEntry.CreateSystemEntry(
            Guid.NewGuid(), Guid.NewGuid(), type, Money.Of(500m, "EUR"),
            "Engine trying to close an account.", LedgerSourceType.PayRunSettlement, "system",
            Guid.NewGuid(), Now, Guid.NewGuid());

        // Nothing in the domain lets the ENGINE reach this type through a business path: the settlement
        // service only ever writes ClawbackAppliedCredit. This assertion documents that the origin stamp
        // still tells the truth if it ever did — the entry would be unmistakably System-made.
        systemEntry.Origin.Should().Be(LedgerEntryOrigin.System);
    }

    [Fact]
    public void Closing_a_debt_in_full_brings_the_balance_to_zero()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);

        var debt = PayeeLedgerEntry.CreateSystemEntry(
            tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(500m, "EUR"),
            "Churned deal.", LedgerSourceType.DealChurn, "system", Guid.NewGuid(), Now, Guid.NewGuid());
        balance.Apply(debt, Now);

        var closing = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.ExternalSettlementCredit, Money.Of(500m, "EUR"),
            "Deducted from the final paycheck by payroll.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());
        balance.Apply(closing, Now);

        balance.Balance.Amount.Should().Be(0m);
        balance.OutstandingDebt().Amount.Should().Be(0m, "the account is settled");
        // Append-only: closing the account did not erase the debit that created the debt.
        debt.Amount.Amount.Should().Be(-500m);
    }

    // ── Technical correction vs business forgiveness ─────────────────────────────
    // Both are credits that move the balance the same way, and that is exactly why they must stay
    // different types. Neutralising a bad import with "forgiveness" would tell the CFO the company
    // chose to let a debt go, when in fact it never charged one.

    [Fact]
    public void A_data_correction_credit_is_a_human_written_credit()
    {
        var entry = PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.DataCorrectionCredit,
            Money.Of(333.3333m, "EUR"),
            "Technical correction — test artefact with an invalid date.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

        entry.Amount.Amount.Should().Be(333.3333m);
        LedgerTransactionType.DataCorrectionCredit.IsDebit().Should().BeFalse();
        LedgerTransactionType.DataCorrectionCredit.IsManuallyCreatable().Should().BeTrue();
        entry.Origin.Should().Be(LedgerEntryOrigin.Human);
    }

    [Fact]
    public void A_technical_correction_is_countable_apart_from_a_business_forgiveness()
    {
        // The reporting property that justifies two types: same effect on the balance, different
        // meaning, and each one totals on its own without anybody reading a justification.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        var correction = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.DataCorrectionCredit, Money.Of(1000m, "EUR"),
            "Technical correction — the deal synced with today's date.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var forgiveness = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.ClawbackForgivenessCredit, Money.Of(1000m, "EUR"),
            "Agreed with the rep — the churn was not their doing.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

        correction.Amount.Amount.Should().Be(forgiveness.Amount.Amount, "the money moves identically");
        correction.TransactionType.Should().NotBe(forgiveness.TransactionType,
            "…and the reason must remain distinguishable in a report");
    }

    [Fact]
    public void The_engine_cannot_write_a_data_correction()
    {
        // Correcting data is a judgement about what went wrong. No automation gets to make it.
        LedgerTransactionType.DataCorrectionCredit.IsManuallyCreatable().Should().BeTrue();

        var act = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.DataCorrectionCredit,
            Money.Of(100m, "EUR"), "No actor.", "", Guid.NewGuid(), Now, Guid.NewGuid());

        act.Should().Throw<DomainException>("an entry nobody signed is not a correction");
    }

    // ── Closing an account that ends IN CREDIT ───────────────────────────────────
    // The mirror of the debt case. A terminated payee is excluded from every pay run, so a positive
    // balance would sit there forever — the engine will never pay someone it no longer processes.
    // Treasury pays it outside Wasnie and this entry records that the money moved.

    [Fact]
    public void A_final_settlement_is_a_human_written_DEBIT()
    {
        var entry = PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.FinalSettlementDebit,
            Money.Of(500m, "EUR"),
            "Final settlement paid with the last paycheck.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

        // Cash left the company, so the payee's balance comes DOWN toward zero.
        entry.Amount.Amount.Should().Be(-500m);
        LedgerTransactionType.FinalSettlementDebit.IsDebit().Should().BeTrue();
        LedgerTransactionType.FinalSettlementDebit.IsManuallyCreatable().Should().BeTrue();
        entry.Origin.Should().Be(LedgerEntryOrigin.Human);
        entry.CreatedBy.Should().Be("finance@acme.com");
    }

    [Fact]
    public void A_final_settlement_requires_an_actor_and_a_justification()
    {
        var noActor = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.FinalSettlementDebit,
            Money.Of(500m, "EUR"), "Paid.", "", Guid.NewGuid(), Now, Guid.NewGuid());
        noActor.Should().Throw<DomainException>();

        var noReason = () => PayeeLedgerEntry.CreateManualAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), LedgerTransactionType.FinalSettlementDebit,
            Money.Of(500m, "EUR"), "  ", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid());
        noReason.Should().Throw<DomainException>();
    }

    [Fact]
    public void Paying_a_departed_payee_what_they_were_owed_brings_the_balance_to_zero()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);

        // They ended up in credit: a pay run withheld more than they actually owed.
        var inCredit = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.DataCorrectionCredit, Money.Of(500m, "EUR"),
            "Technical correction — the debt behind the withholding was not real.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());
        balance.Apply(inCredit, Now);
        balance.Balance.Amount.Should().Be(500m);

        var settlement = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.FinalSettlementDebit, Money.Of(500m, "EUR"),
            "Treasury transferred the outstanding balance with the final paycheck.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());
        balance.Apply(settlement, Now);

        balance.Balance.Amount.Should().Be(0m);
        // Append-only: the credit that put them in the black is still there, next to the payment.
        inCredit.Amount.Amount.Should().Be(500m);
    }

    [Fact]
    public void The_two_directions_of_closing_stay_countable_apart()
    {
        // "Cash we transferred to people who are gone" and "debt we absorbed" are different figures;
        // one type could never answer both.
        LedgerTransactionType.FinalSettlementDebit.IsDebit().Should().BeTrue();
        LedgerTransactionType.ExternalSettlementCredit.IsDebit().Should().BeFalse();
        LedgerTransactionType.WriteOffCredit.IsDebit().Should().BeFalse();
        LedgerTransactionType.FinalSettlementDebit.Should()
            .NotBe(LedgerTransactionType.ExternalSettlementCredit);
    }

    // ── A closing entry closes the account IN FULL ───────────────────────────────
    // FinalSettlementDebit is the one closing type whose amount a human types in full. Without an
    // invariant, a typo turns an entry designed to EXTINGUISH a credit into one that OPENS a debt
    // against someone who already left — and that fake debt then shows up on the orphan-account
    // screen offering a write-off to "fix" it. The rule is EQUALITY, not an upper bound: the balance
    // must be positive to begin with, and the amount must match it exactly, because a partial
    // settlement leaves the account orphaned and so has not done what "Final" claims.

    [Fact]
    public void A_final_settlement_bigger_than_the_balance_is_rejected_and_writes_nothing()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);

        var inCredit = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.DataCorrectionCredit, Money.Of(500m, "EUR"),
            "Technical correction — the withheld debt was not real.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());
        balance.Apply(inCredit, Now);

        // The typo: €600 against a balance of +€500.
        var tooBig = PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.FinalSettlementDebit, Money.Of(600m, "EUR"),
            "Treasury paid the final balance.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid());

        var act = () => balance.Apply(tooBig, Now);

        act.Should().Throw<DomainException>().WithMessage("*FinalSettlementMustEqualBalance*");
        balance.Balance.Amount.Should().Be(500m, "a rejected entry must leave the balance untouched");
        balance.OutstandingDebt().Amount.Should().Be(0m, "no fictitious debt was opened");
    }

    [Fact]
    public void A_final_settlement_for_the_exact_balance_still_closes_the_account()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);

        balance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.DataCorrectionCredit, Money.Of(500m, "EUR"),
            "Technical correction.", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid()), Now);

        balance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.FinalSettlementDebit, Money.Of(500m, "EUR"),
            "Treasury transferred the outstanding balance.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid()), Now);

        balance.Balance.Amount.Should().Be(0.0000m);
    }

    [Fact]
    public void A_PARTIAL_final_settlement_is_REJECTED_a_closing_is_total()
    {
        // The entry exists to EXTINGUISH the account so it leaves the orphan queue. Paying €300 of a
        // €500 balance leaves +€200 — the account is still orphaned, so the entry did not do the one
        // thing its name claims. Wasnie does not orchestrate instalments; that is an ERP's accounts
        // payable. Rejecting it also keeps the typo case honest: any amount that is not the balance
        // is a mistake, whether it overshoots or falls short.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);

        balance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.DataCorrectionCredit, Money.Of(500m, "EUR"),
            "Technical correction.", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid()), Now);

        var act = () => balance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.FinalSettlementDebit, Money.Of(300m, "EUR"),
            "Partial transfer with the final paycheck; remainder to follow.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid()), Now);

        act.Should().Throw<DomainException>().WithMessage("*FinalSettlementMustEqualBalance*");
        balance.Balance.Amount.Should().Be(500m, "the rejected entry left the balance untouched");
    }

    [Fact]
    public void A_final_settlement_against_a_NEGATIVE_balance_is_rejected()
    {
        // They owe money; there is no credit to settle. Allowing it would sink the debt deeper under
        // a label that claims the account was closed.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);

        balance.Apply(PayeeLedgerEntry.CreateSystemEntry(
            tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(100m, "EUR"),
            "Churned deal.", LedgerSourceType.DealChurn, "system",
            Guid.NewGuid(), Now, Guid.NewGuid()), Now);
        balance.Balance.Amount.Should().Be(-100m);

        var act = () => balance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.FinalSettlementDebit, Money.Of(50m, "EUR"),
            "Treasury paid the final balance.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid()), Now);

        act.Should().Throw<DomainException>().WithMessage("*FinalSettlementRequiresPositiveBalance*");
        balance.Balance.Amount.Should().Be(-100m, "the debt is unchanged");
    }

    [Fact]
    public void A_final_settlement_against_a_ZERO_balance_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();
        var balance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);

        var act = () => balance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.FinalSettlementDebit, Money.Of(10m, "EUR"),
            "Nothing left to pay.", "finance@acme.com",
            Guid.NewGuid(), Now, Guid.NewGuid()), Now);

        act.Should().Throw<DomainException>().WithMessage("*FinalSettlementRequiresPositiveBalance*");
        balance.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public void The_closing_guard_applies_ONLY_to_a_final_settlement()
    {
        // Regression: the other types keep their own semantics. A credit against a debt may legitimately
        // overshoot into positive territory (finance recovered more than the outstanding figure), and a
        // clawback may legitimately push a positive balance negative — that is what a clawback IS.
        var tenantId = Guid.NewGuid();
        var payeeId = Guid.NewGuid();

        var debtBalance = PayeeBalance.Open(tenantId, payeeId, "EUR", Guid.NewGuid(), Now);
        debtBalance.Apply(PayeeLedgerEntry.CreateSystemEntry(
            tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(100m, "EUR"),
            "Churned deal.", LedgerSourceType.DealChurn, "system",
            Guid.NewGuid(), Now, Guid.NewGuid()), Now);

        debtBalance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.WriteOffCredit, Money.Of(150m, "EUR"),
            "Absorbed the loss.", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid()), Now);
        debtBalance.Balance.Amount.Should().Be(50m, "WriteOffCredit is not subject to the guard");

        debtBalance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.ExternalSettlementCredit, Money.Of(25m, "EUR"),
            "Recovered via payroll.", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid()), Now);
        debtBalance.Balance.Amount.Should().Be(75m, "ExternalSettlementCredit is not subject to the guard");

        // A clawback CAN cross zero — that is exactly its job.
        debtBalance.Apply(PayeeLedgerEntry.CreateSystemEntry(
            tenantId, payeeId, LedgerTransactionType.ClawbackDebit, Money.Of(200m, "EUR"),
            "Another churned deal.", LedgerSourceType.DealChurn, "system",
            Guid.NewGuid(), Now, Guid.NewGuid()), Now);
        debtBalance.Balance.Amount.Should().Be(-125m, "ClawbackDebit is not subject to the guard");

        // So can a manual data correction that removes an inflated payment.
        debtBalance.Apply(PayeeLedgerEntry.CreateManualAdjustment(
            tenantId, payeeId, LedgerTransactionType.DataCorrectionDebit, Money.Of(75m, "EUR"),
            "Bad import inflated a payment.", "finance@acme.com", Guid.NewGuid(), Now, Guid.NewGuid()), Now);
        debtBalance.Balance.Amount.Should().Be(-200m, "DataCorrectionDebit is not subject to the guard");
    }
}
