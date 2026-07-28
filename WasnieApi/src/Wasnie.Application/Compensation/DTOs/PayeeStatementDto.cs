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

    // ── Cash flow (absolute values; the minus sign lives in the operator on screen) ──
    decimal CommissionsThisPeriod,
    decimal RetentionApplied,
    decimal NetPayable,

    // ── Balance movement (signed: debt is negative, amortization positive) ──
    decimal PreviousDebt,
    decimal Amortization,
    decimal NewCarryover,

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
    decimal? SourceCommissionAmount);
