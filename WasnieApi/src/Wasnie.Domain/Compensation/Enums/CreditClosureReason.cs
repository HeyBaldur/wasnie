namespace Wasnie.Domain.Compensation.Enums;

/// <summary>
/// Why a credit left circulation without ever being paid through a payout.
///
/// ★★ TWO VALUES, NOT ONE, AND FOR THE SAME REASON THE LEDGER HAS TWO CLOSING TYPES. "We recovered it
/// through HR" and "we ate the loss" are different facts about the business, and a CFO has to be able
/// to total each without mining free text. One generic "closed" value would force exactly that mining,
/// and the note is a sentence a person typed — not something to aggregate.
///
/// ★ AND NEITHER IS <c>Consumed</c> OR <c>Superseded</c>. Those two already mean something: consumed is
/// "a Paid payout took it" (and carries the payout id that proves it), superseded is "a reallocation
/// replaced it" (and the attainment queries read that as stale-because-another-one-exists). A credit
/// closed here was replaced by nothing and paid by nothing — it is a third ending, and giving it one of
/// the other two names would make both of them lie
/// (docs/DIAG_ORPHAN_ACCOUNT_CLOSURE.md §3.1).
/// </summary>
public enum CreditClosureReason
{
    /// <summary>
    /// The commission was settled OUTSIDE Wasnie — typically paid with the departed payee's final
    /// paycheck by payroll. The money reached the person; it just did not travel through a pay run,
    /// because the engine no longer processes anyone who has left.
    /// </summary>
    ExternalSettlement = 0,

    /// <summary>
    /// The company is not paying it. A real cost decision — and the one this whole subsystem is careful
    /// about, because it destroys a claim a person had.
    /// </summary>
    WrittenOff = 1,
}
