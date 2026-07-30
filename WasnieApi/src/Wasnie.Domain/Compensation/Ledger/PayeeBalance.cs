using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Ledger;

/// <summary>
/// Running balance of a payee's ledger, per currency. A PROJECTION of
/// <see cref="PayeeLedgerEntry"/> — the entries are the truth, this row exists for two reasons:
///   1. reading the balance must not scan the whole ledger on every pay run;
///   2. it carries the <see cref="RowVersion"/> that makes concurrent settlement safe.
///
/// Invariant: <see cref="Balance"/> == sum of every entry for (TenantId, PayeeId, Currency).
/// Which is why <see cref="Apply"/> is the ONLY way to move it — there is no setter, and the
/// engine never writes an entry without applying it to the balance in the same SaveChanges.
///
/// Sign: negative = the payee owes that much (outstanding clawback). Zero = settled.
/// Positive = Wasnie owes the payee an adjustment on top of the normal commissions.
///
/// CONCURRENCY: <see cref="RowVersion"/> is a real SQL Server rowversion (same mechanism as
/// <see cref="Wasnie.Domain.Compensation.Credits.Credit"/>). A manual adjustment landing while a
/// pay run is settling makes the pay run's UPDATE fail with DbUpdateConcurrencyException instead
/// of silently overwriting a balance computed from stale data. NOTE: EF InMemory does not generate
/// rowversion values, so this guard can only be verified against real SQL (integration tests).
/// </summary>
public sealed class PayeeBalance : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public Guid PayeeId { get; private set; }

    /// <summary>ISO currency of this balance. One row per (payee, currency) — see PayeeLedgerEntry remarks.</summary>
    public string Currency { get; private set; } = string.Empty;

    public Money Balance { get; private set; } = null!;

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token — EF detects a concurrent write on this balance.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private PayeeBalance() { }

    public static PayeeBalance Open(
        Guid tenantId, Guid payeeId, string currency, Guid id, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (payeeId == Guid.Empty)
            throw new DomainException("PayeeId must not be empty.");

        // Money.Of validates the ISO shape; reuse it so there is one currency rule in the system.
        var zero = Money.Zero(currency);

        return new PayeeBalance
        {
            Id = id,
            TenantId = tenantId,
            PayeeId = payeeId,
            Currency = zero.Currency,
            Balance = zero,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Applies one ledger entry. The entry's amount is already signed, so this is a plain add —
    /// the accounting meaning lives in the entry, not here.
    /// </summary>
    public void Apply(PayeeLedgerEntry entry, DateTimeOffset now)
    {
        if (entry.PayeeId != PayeeId)
            throw new DomainException(
                $"Ledger entry belongs to payee {entry.PayeeId} and cannot be applied to the balance of {PayeeId}.");
        if (entry.TenantId != TenantId)
            throw new DomainException("Ledger entry belongs to a different tenant.");

        // Money.Add throws on a currency mismatch. Surfaced with the business reason so the caller
        // learns WHY it is impossible rather than reading a raw arithmetic error.
        if (entry.Amount.Currency != Currency)
            throw new DomainException(
                $"Cannot apply a {entry.Amount.Currency} entry to the {Currency} balance of payee {PayeeId}. " +
                "Balances are per currency because Wasnie holds no exchange rates.");

        // A final settlement EXISTS to EXTINGUISH the account so it leaves the orphan-account queue.
        // Two things follow, and both are invariants rather than validations:
        //
        //   1. It only applies to a POSITIVE balance. Against zero there is nothing owed, and against
        //      a negative one it would sink the debt deeper under a label claiming the account closed.
        //   2. The amount must EQUAL the balance exactly. The amount is typed by hand (Origin=Human),
        //      so a typo — 600 against +500 — would otherwise flip the balance to −100 and invent a
        //      debt against someone who already left, which in turn lights up the orphan-account
        //      screen offering a write-off for a debt that never existed.
        //
        // Equality, NOT an upper bound: a PARTIAL settlement leaves a positive remainder, so the
        // account is still orphaned and the entry has not done the one job its name claims. Wasnie
        // does not orchestrate instalments — that is an ERP's accounts-payable, not a commission
        // engine. A closing is total, or it is not a closing.
        if (entry.TransactionType == Enums.LedgerTransactionType.FinalSettlementDebit)
        {
            // The entry is stored signed (negative); compare magnitudes against the positive balance.
            var magnitude = entry.Amount.Abs();

            if (Balance.Amount <= 0m)
                throw new DomainException(
                    "FinalSettlementRequiresPositiveBalance: a final settlement closes money the company " +
                    $"still owes a departed payee. The balance of payee {PayeeId} is {Balance.Amount} " +
                    $"{Currency}, so there is nothing to settle.");

            if (magnitude.Amount != Balance.Amount)
                throw new DomainException(
                    "FinalSettlementMustEqualBalance: a final settlement closes the account in full, so it " +
                    $"must equal the balance exactly. {magnitude.Amount} {Currency} was entered against a " +
                    $"balance of {Balance.Amount} {Currency}.");
        }

        Balance = Balance.Add(entry.Amount);
        UpdatedAt = now;
    }

    /// <summary>Outstanding debt as a positive figure (0 when the payee owes nothing).</summary>
    public Money OutstandingDebt() =>
        Balance.Amount < 0m ? Balance.Abs() : Money.Zero(Currency);
}
