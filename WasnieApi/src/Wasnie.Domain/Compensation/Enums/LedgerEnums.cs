namespace Wasnie.Domain.Compensation.Enums;

/// <summary>
/// WHO produced a ledger entry. Immutable, set by the factory — never by the caller.
/// Separated from <see cref="LedgerTransactionType"/> on purpose: "who" and "what it means
/// accounting-wise" are different questions, and reports need both independently.
/// </summary>
public enum LedgerEntryOrigin
{
    /// <summary>The engine wrote it (clawback trigger, pay-run settlement). No human chose the number.</summary>
    System = 0,

    /// <summary>A person wrote it through the manual-adjustment endpoint. Always carries an actor + justification.</summary>
    Human = 1,
}

/// <summary>
/// WHAT an entry means accounting-wise. The sign of the entry is DERIVED from this value
/// (see <see cref="LedgerTransactionTypeExtensions.IsDebit"/>) so a debit can never be stored
/// as a positive amount by mistake.
/// </summary>
public enum LedgerTransactionType
{
    /// <summary>Commission already paid that is being taken back (churn, origin error). Negative.</summary>
    ClawbackDebit = 0,

    /// <summary>A human forgives part or all of an outstanding clawback. Positive.</summary>
    ClawbackForgivenessCredit = 1,

    /// <summary>A human grants an amount outside the plan rules. Positive.</summary>
    ManualBonusCredit = 2,

    /// <summary>A human corrects bad data that inflated a payment. Negative.</summary>
    DataCorrectionDebit = 3,

    /// <summary>
    /// The engine actually withheld debt from a payment. Positive: the debt was collected, so the
    /// balance moves back toward zero. Without this type the ledger could not close — the four
    /// originating types above only ever create or forgive debt, never settle it. Written by the
    /// pay-run settlement, in the same SaveChanges that consumes the credits.
    /// </summary>
    ClawbackAppliedCredit = 4,

    // ── Closing the account of someone who has left ───────────────────────────────────────────
    // A terminated payee's debt is frozen, not forgiven and not left circulating. These two types are
    // the ONLY ways it leaves the books, and they are deliberately separate: "we recovered it through
    // HR" and "we ate the loss" are different facts about the business. A CFO must be able to total
    // each without mining free text, which is exactly what one generic "closing credit" would force.
    //
    // Wasnie records the decision; it does not make it and does not collect. Whether the money is
    // taken from a final paycheck, sent to collections, or written off happens in HR/finance/legal,
    // with data Wasnie does not hold.

    /// <summary>
    /// The debt was recovered OUTSIDE Wasnie — typically deducted from the final paycheck by payroll.
    /// Positive: the money came back, so the balance moves toward zero. Human-only: no engine can know
    /// that a settlement happened somewhere else.
    /// </summary>
    ExternalSettlementCredit = 5,

    /// <summary>
    /// The company absorbed the loss: the debt is uncollectable and is being written off. Positive for
    /// the payee's balance, and a real cost for the business — which is why it is its own type and
    /// never blended with a settlement that actually recovered cash.
    /// </summary>
    WriteOffCredit = 6,

    /// <summary>
    /// The credit half of <see cref="DataCorrectionDebit"/>: a human neutralising an entry that a
    /// TECHNICAL fault produced — a deal synced with a wrong date, a test artefact, an import that
    /// double-counted.
    ///
    /// Deliberately NOT <see cref="ClawbackForgivenessCredit"/>, which is a BUSINESS decision: someone
    /// with authority chose to let a real debt go. Using forgiveness to erase a data error would tell
    /// the CFO the company forgave money it never actually charged, and no amount of free-text
    /// justification recovers that distinction once the totals are added up.
    /// </summary>
    DataCorrectionCredit = 7,
}

/// <summary>What triggered a System entry — kept so a clawback can be traced back to its cause.</summary>
public enum LedgerSourceType
{
    /// <summary>Deal went from won to lost inside the maturation window — proportional clawback.</summary>
    DealChurn = 0,

    /// <summary>The contract was never real (origin error) — 100% clawback, not proportional.</summary>
    OriginError = 1,

    /// <summary>A pay run withheld outstanding debt from a payment.</summary>
    PayRunSettlement = 2,
}

public static class LedgerTransactionTypeExtensions
{
    /// <summary>
    /// True when the type reduces what the payee is owed (stored as a negative amount).
    /// The single place the sign convention lives — the entry factory derives the sign from here,
    /// so an inconsistent (type, sign) pair is unrepresentable.
    /// </summary>
    public static bool IsDebit(this LedgerTransactionType type) => type switch
    {
        LedgerTransactionType.ClawbackDebit => true,
        LedgerTransactionType.DataCorrectionDebit => true,
        LedgerTransactionType.ClawbackForgivenessCredit => false,
        LedgerTransactionType.ManualBonusCredit => false,
        LedgerTransactionType.ClawbackAppliedCredit => false,
        // Both closing types return money to the balance: one because it was recovered elsewhere,
        // one because the company absorbed it. Either way the payee owes that much less.
        LedgerTransactionType.ExternalSettlementCredit => false,
        LedgerTransactionType.WriteOffCredit => false,
        LedgerTransactionType.DataCorrectionCredit => false,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown ledger transaction type."),
    };

    /// <summary>
    /// True for the types a human is allowed to create through the manual-adjustment endpoint.
    /// ClawbackDebit and ClawbackAppliedCredit are engine-only: a person must never hand-write a
    /// clawback or a settlement — those come from a trigger and from a payment respectively.
    /// </summary>
    public static bool IsManuallyCreatable(this LedgerTransactionType type) => type switch
    {
        LedgerTransactionType.ClawbackForgivenessCredit => true,
        LedgerTransactionType.ManualBonusCredit => true,
        LedgerTransactionType.DataCorrectionDebit => true,
        // Closing a departed payee's account is a finance DECISION, so only a person may write it —
        // and the manual path is what forces an actor and a justification onto the entry.
        LedgerTransactionType.ExternalSettlementCredit => true,
        LedgerTransactionType.WriteOffCredit => true,
        LedgerTransactionType.DataCorrectionCredit => true,
        _ => false,
    };
}
