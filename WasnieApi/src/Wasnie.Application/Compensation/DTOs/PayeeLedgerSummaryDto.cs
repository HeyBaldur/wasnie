namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// What a payee EARNED set against what they OWE — the two halves that live in different tables and
/// that nobody had ever put in one answer.
///
/// ★★ THE FALSE ZERO IS WHAT THIS TYPE EXISTS TO PREVENT. The ledger (PayeeBalance) records only debts:
/// clawbacks, adjustments, settlements. A rep who earned 10,000 and never had a clawback has a ledger
/// balance of exactly 0.00, and that zero means "owes the company nothing" — NOT "is paid nothing".
/// Anything that reads the ledger alone and reports "your balance is 0" tells a salesperson they have
/// no money coming. That is the single most damaging sentence this product could generate, and it is
/// one plausible query away, which is why the earned side is not optional here: the two numbers are
/// born together or the type does not exist.
///
/// ★ PER CURRENCY, NEVER BLENDED. Wasnie holds no FX rates (Spec §5b.5). A payee earning USD and owing
/// EUR gets two rows, and no line anywhere adds them.
/// </summary>
/// <param name="PeriodLabel">
/// The window the period-scoped figures cover, echoed back exactly as it was resolved ("this-month",
/// "ytd", "all-time"). Echoed rather than assumed because a reader that guesses the window will
/// eventually describe last quarter's money as this quarter's.
/// </param>
public sealed record PayeeLedgerSummaryDto(
    Guid PayeeId,
    string PayeeName,
    string PeriodLabel,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    IReadOnlyList<PayeeCurrencyBalanceDto> ByCurrency);

/// <summary>
/// One currency's story, split into figures that ARE period-scoped and figures that CANNOT be.
///
/// ★ WHY THE SPLIT IS NOT COSMETIC. The ledger has no period dimension at all — an entry carries the
/// date it was booked and the date the real-world event happened, and a balance is a running total with
/// no concept of a cycle. So "how much did they owe in March" is not a question the data can answer, and
/// inventing an answer by filtering entries on CreatedAt would produce a figure that looks period-scoped
/// and is not. <see cref="OutstandingDebt"/> and everything derived from it are therefore AS OF NOW,
/// stated as such, sitting next to the period figures rather than pretending to be one of them.
/// </summary>
/// <param name="EarnedCommissionsInPeriod">
/// ACCRUED commission for payouts whose compensation period intersects the window: Calculated +
/// Approved + Paid. This is "what the plan rules say they made", not "what left the bank" — the payouts
/// screen's own period filter, so the tool and the screen agree.
///
/// Note the intersection: a payout covering a whole quarter counts IN FULL against a one-month window.
/// Correct for one window (it is the payout that covers your month); do NOT sum these across
/// consecutive windows, or the same euros are counted more than once.
/// </param>
/// <param name="PaidOutInPeriod">
/// CASH that actually moved inside the window: Status = Paid, attributed by PaidAt. The same rule the
/// dashboard's treasury card uses — never Period.End, which reports July's money in December.
/// </param>
/// <param name="DisputedInPeriod">
/// Payouts under dispute in the window. Deliberately EXCLUDED from
/// <see cref="EarnedCommissionsInPeriod"/> and reported on its own: folding a contested figure into
/// "earned" states a conclusion the business has not reached, and dropping it silently loses money
/// somebody is arguing about.
/// </param>
/// <param name="AwaitingPaymentAllTime">
/// Accrued but not yet paid, across EVERY period — Calculated + Approved, unfiltered by the window.
/// All-time on purpose: money earned last quarter and still unpaid is money still owed today, and a
/// window-scoped version of this figure would hide it precisely when it matters most.
/// </param>
/// <param name="OutstandingDebt">
/// What the payee owes the company right now, as a POSITIVE magnitude (0 when they owe nothing). The
/// ledger stores it negative; the sign is normalised here so no reader has to remember the convention.
/// </param>
/// <param name="NetPendingPayout">
/// <see cref="AwaitingPaymentAllTime"/> − <see cref="OutstandingDebt"/>: what the payee can actually
/// expect to receive. Negative means the debt outruns everything currently pending.
///
/// Both inputs are as-of-now, and that is the point — subtracting a live debt from a period-scoped
/// earnings figure would produce a number that is true of no moment in time.
/// </param>
/// <param name="Interpretation">
/// A token, not a sentence — see <see cref="BalanceSemantic"/>.
/// </param>
public sealed record PayeeCurrencyBalanceDto(
    string Currency,
    decimal EarnedCommissionsInPeriod,
    decimal PaidOutInPeriod,
    decimal DisputedInPeriod,
    decimal AwaitingPaymentAllTime,
    decimal OutstandingDebt,
    decimal NetPendingPayout,
    BalanceSemantic Interpretation);

/// <summary>
/// WHICH STORY the numbers tell, decided here and rendered by the model.
///
/// ★ A TOKEN, NOT PROSE — the same discipline as GetPlanRulesTool's rate semantics. An English sentence
/// written in this assembly would have to be translated for the Spanish and Polish users the product
/// already has, and presentation would have moved into the domain. The system prompt teaches these five
/// words once; the model renders them in whatever language the user is writing.
///
/// It also removes the one inference that must never be left to a language model: deciding, from a zero,
/// WHICH zero it is looking at.
/// </summary>
public enum BalanceSemantic
{
    /// <summary>No payouts and no debt in this currency. The only case where "nothing to report" is true.</summary>
    NothingRecorded = 0,

    /// <summary>
    /// ★ THE FALSE ZERO, NAMED. Money earned, no debt whatsoever — so the ledger balance is 0.00 and
    /// that zero is GOOD NEWS. The token exists so the model is never the thing that has to work out
    /// that this zero means "owes nothing", not "gets nothing".
    /// </summary>
    EarningsAndNoDebt = 1,

    /// <summary>Earnings and a debt, with enough pending to cover it.</summary>
    EarningsWithDebt = 2,

    /// <summary>A debt with nothing pending to net it against — the payee owes and is not owed.</summary>
    DebtOnly = 3,

    /// <summary>The debt exceeds everything pending: NetPendingPayout is negative and carries over.</summary>
    DebtExceedsPending = 4,
}
