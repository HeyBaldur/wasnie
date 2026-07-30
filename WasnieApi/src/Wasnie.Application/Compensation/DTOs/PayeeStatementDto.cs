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
/// One departed payee whose account is still open. <see cref="Balance"/> is signed exactly as stored —
/// negative means they owe it. A positive balance appears here too: money Wasnie still owes someone who
/// has left is just as unfinished as money they owe, and hiding it would be the same mistake.
/// </summary>
public sealed record TerminatedPayeeBalanceDto(
    Guid PayeeId,
    string PayeeName,
    string EmployeeCode,
    DateOnly? TerminationDate,
    decimal Balance,
    string Currency,
    DateTimeOffset BalanceUpdatedAt);
