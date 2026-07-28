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
}
