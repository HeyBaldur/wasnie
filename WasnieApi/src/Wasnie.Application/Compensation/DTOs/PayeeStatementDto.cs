namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// A payee's account statement for one currency: the two equations the screen draws, with EVERY
/// number already computed server-side.
///
/// The screen must never derive one field from another. <see cref="RetentionApplied"/> and
/// <see cref="Amortization"/> carry the SAME magnitude with opposite meaning — money leaving the
/// payee's pocket, and the same money paying down the balance — and each travels with the sign of
/// its own equation. A client that computed one from the other would become a second source of
/// truth against PayRunSettlement, and the two would drift.
///
/// Cash-flow equation:  Commissions − Retention = NetPayable   (absolute values)
/// Balance equation:    PreviousDebt + Amortization = NewCarryover   (signed)
/// </summary>
public sealed record PayeeStatementDto(
    Guid PayeeId,
    string PayeeName,
    string Currency,

    // ── Cash flow of the settled run (absolute values; the minus sign lives in the operator) ──
    // Null when no run has settled yet. They used to be zeros, which reads as "this person earned
    // nothing and took nothing home" when the truth is "no pay run has closed against this balance".
    decimal? CommissionsThisPeriod,
    decimal? RetentionApplied,
    decimal? NetPayable,

    /// <summary>
    /// The payee's balance RIGHT NOW, straight from <c>PayeeBalance</c>: the sum of every entry in
    /// their ledger. Always populated, settlement or not — this is the one figure that answers "what
    /// do they owe today", and the screen leads with it.
    ///
    /// It is deliberately NOT derived from the settlement below. The settlement is a photograph of one
    /// payment; entries added after it (a churn debit that synced an hour later, a manual adjustment)
    /// move this number and must not move that one.
    /// </summary>
    decimal CurrentBalance,

    // ── The settled pay run: a PHOTOGRAPH, not the present ────────────────────────────────────
    // All three describe the run named by PayRunId/SettledAt and never change afterwards — rewriting
    // the history of a payment that already happened is exactly what must not occur. They are null
    // when no run has settled against this balance yet.
    decimal? PreviousDebt,
    decimal? Amortization,
    /// <summary>
    /// The debt left over AT THE CLOSE OF THAT RUN. It used to double as the live balance whenever no
    /// settlement existed, so the same field meant two different things and no client could tell which
    /// one it had received. It now means one thing; the live figure is <see cref="CurrentBalance"/>.
    /// </summary>
    decimal? NewCarryover,

    /// <summary>
    /// The cap that was in force, when it is unambiguous. Null when the payee's payouts in this run
    /// came from plans with DIFFERENT caps — there is no single percentage to name, and inventing
    /// one would put a wrong number in an explanatory sentence about someone's pay.
    /// </summary>
    decimal? CapPercentApplied,

    /// <summary>
    /// True when a cap actually stopped the debt from being collected in full — debt survived this
    /// run even though the payee was still paid something. Drives the extra explanatory sentence.
    /// </summary>
    bool CapLimited,

    /// <summary>Null when this payee has no settled run yet — the screen shows the balance only.</summary>
    Guid? PayRunId,
    DateTimeOffset? SettledAt);

/// <summary>One ledger row as the table renders it. Amount is signed exactly as stored.</summary>
public sealed record PayeeLedgerEntryDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Origin,
    string TransactionType,
    decimal Amount,
    string Currency,
    string Justification,
    string CreatedBy,
    string? SourceExternalDealId,
    Guid? SourceTransactionId,
    int? DaysActive,
    int? MaturationDays,
    decimal? SourceCommissionAmount,
    /// <summary>
    /// The date the deal was actually lost in the CRM (null for entries no CRM event produced). It is a
    /// TYPED field, not a phrase inside <c>Justification</c>: the screen shows it in its own column and
    /// formats it in the reader's locale instead of parsing a sentence. Distinct from
    /// <c>CreatedAt</c>, which is when Wasnie booked the entry.
    /// </summary>
    DateOnly? EventDate,
    /// <summary>The plan whose clawback policy produced this entry (null when no plan did). Typed for
    /// the same reason: attributing a debit is a lookup, not a text search.</summary>
    Guid? SourcePlanId);

/// <summary>
/// One departed payee's open account, in ONE currency. "Open" now has TWO independent meanings, and a
/// row appears when EITHER is true:
///
///   • <see cref="Balance"/> — the ledger balance, signed exactly as stored (negative = they owe it).
///     A positive balance appears here too: money Wasnie still owes someone who has left is just as
///     unfinished as money they owe.
///
///   • <see cref="UnsettledCredits"/> — commission they EARNED that never reached a payout. This half
///     was invisible until now, and the reason is worth stating: the ledger records what a payee OWES,
///     so an unconsumed credit produces no <c>PayeeBalance</c> row at all. Queueing off balances meant
///     a departed payee with earned-but-unpaid commission and no debt had a balance of "nothing" and
///     was therefore, to this screen, settled.
///
/// ★ THE TWO ARE NEVER ADDED TOGETHER. They point in opposite directions and come from different
/// sources; one number covering both would be a figure with no meaning. They are reported side by side
/// and the reader decides.
/// </summary>
public sealed record TerminatedPayeeBalanceDto(
    Guid PayeeId,
    string PayeeName,
    string EmployeeCode,
    DateOnly? TerminationDate,
    decimal Balance,
    string Currency,
    /// <summary>Null when there is no ledger balance row at all — which is the ordinary case for a
    /// payee whose only open item is unsettled commission. NOT a zero: "never had a balance" and
    /// "balance updated to zero" are different facts.</summary>
    DateTimeOffset? BalanceUpdatedAt,
    /// <summary>
    /// When this payee's account was closed, if it ever was — and if this row exists at all, then
    /// something arrived AFTER that.
    ///
    /// ★ IT IS NOT A FILTER. Queue membership stays derived from money, so a closed account leaves the
    /// list because it is empty, not because a flag hides it. That is deliberate: the product allows a
    /// credit to arrive after someone leaves, so filtering on closure would make the new money invisible
    /// all over again — the exact bug this queue was built to close. Non-null here therefore means
    /// "closed once, and reopened by something new", which is a sentence the screen can show.
    /// </summary>
    DateTimeOffset? AccountClosedAt,
    /// <summary>Sum of <see cref="UnsettledCredits"/>. Server-side because a screen must not add money.</summary>
    decimal UnsettledCreditTotal,
    IReadOnlyList<UnsettledCreditDto> UnsettledCredits);

/// <summary>
/// One commission credit that was earned and never paid: active (not superseded) and unconsumed by any
/// payout. Carries what a person needs to ACT on it — who, how much, under which plan and rule, when it
/// was credited, and the sale it came from — because a bare total is something to worry about rather
/// than something to work.
/// </summary>
public sealed record UnsettledCreditDto(
    Guid CreditId,
    decimal Amount,
    string Currency,
    string PlanName,
    string RuleName,
    DateOnly AllocatedAt,
    Guid TransactionId,
    string TransactionReference);

/// <summary>
/// The whole queue: the rows, plus the totals that make somebody look at it.
///
/// ★ TOTALS ARE PER CURRENCY AND NEVER BLENDED. Wasnie holds no exchange rates, so a single figure
/// across currencies would be invented. Same rule the balance list already follows by emitting one row
/// per (payee, currency).
/// </summary>
public sealed record TerminatedAccountsDto(
    IReadOnlyList<TerminatedPayeeBalanceDto> Rows,
    IReadOnlyList<TerminatedAccountsTotalDto> Totals);

/// <summary>
/// What is outstanding in ONE currency. <see cref="UnsettledCreditTotal"/> is the money this queue
/// exists to surface: commission earned by people who have left and that no pay run will ever pick up.
///
/// ★ THE LEDGER BALANCES ARE DELIBERATELY NOT TOTALLED HERE. They carry both signs — a debt to recover
/// and a liability to pay — and summing them would net a debt against a liability into a number that
/// describes neither. Counting them is safe; adding them is not.
/// </summary>
public sealed record TerminatedAccountsTotalDto(
    string Currency,
    decimal UnsettledCreditTotal,
    int UnsettledCreditCount,
    int PayeeCount);
