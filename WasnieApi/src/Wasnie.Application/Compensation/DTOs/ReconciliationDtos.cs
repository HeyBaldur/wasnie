namespace Wasnie.Application.Compensation.DTOs;

/// <summary>What kind of thing a queue row is. Decides which identifiers and links make sense.</summary>
public enum ReconciliationEntryKind
{
    /// <summary>A credit the engine allocated but refused to price. Carries the sale it came from.</summary>
    Credit,

    /// <summary>A transaction that could not be turned into a credit at all.</summary>
    Transaction,

    /// <summary>A plan that is Active and pays nothing. Has no payee and no amount.</summary>
    Plan,
}

/// <summary>
/// Which pot a row's money belongs to.
///
/// ★★ THE TWO POTS ARE NEVER ADDED. Unpaid commission is money the company still owes; a clawback is
/// money it is owed back. A single "net" figure would be arithmetically tidy and financially
/// meaningless — it would let a large clawback hide a large unpaid balance, and the screen exists
/// precisely so a CFO can say "I owe exactly this". They are carried as separate fields all the way
/// to the card.
/// </summary>
public enum ReconciliationMoneyKind
{
    /// <summary>No money figure applies to this row (a drifted deal, a plan with no live rules).</summary>
    None,

    /// <summary>
    /// The sale value that could not be converted into a payment.
    ///
    /// ★ IT IS THE BASE, NOT A COMMISSION, AND THE SCREEN MUST SAY SO. When the engine refuses, the
    /// commission is precisely the number nobody knows — that is what the refusal means. Presenting a
    /// derived "amount owed" here would invent the figure KAN-26 exists to stop inventing, so the
    /// honest quantity is the sale at stake.
    /// </summary>
    AffectedBase,

    /// <summary>Commission already paid on a deal that was later lost. Recovered, not owed.</summary>
    Clawback,
}

/// <summary>
/// One entry in the reconciliation queue.
///
/// ★ <see cref="Reasons"/> IS A LIST, AND THE ROW IS STILL ONE ROW. An entry that fails for two
/// reasons appears once carrying both; it is counted once in the totals and once under each reason.
/// Emitting it twice would double the money.
/// </summary>
public sealed record ReconciliationRowDto(
    ReconciliationEntryKind Kind,
    Guid EntityId,
    // The transaction ReferenceNumber belongs to.
    //
    // ★ IT IS NOT ALWAYS EntityId. On a Credit row the entity is the CREDIT and the reference is its
    // TRANSACTION's, so a link built from the entity id would carry the reader to a transaction route
    // holding a credit's id. A link must go where its text says it goes. Null on Plan rows, which
    // have no transaction behind them.
    Guid? TransactionId,
    string? ReferenceNumber,
    Guid? PayeeId,
    string? PayeeName,
    string? PayeeCode,
    Guid? PlanId,
    string? PlanName,
    decimal? Amount,
    string? Currency,
    ReconciliationMoneyKind MoneyKind,
    DateOnly? PeriodDate,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Money held up, per currency. ★ Two figures, never a net — see <see cref="ReconciliationMoneyKind"/>.
/// </summary>
public sealed record ReconciliationCurrencyTotalDto(
    string Currency,
    decimal AffectedBaseAmount,
    decimal ClawbackAmount,
    int RowCount);

/// <summary>How many entries carry a given reason. A two-reason entry counts under both.</summary>
public sealed record ReconciliationReasonCountDto(string Reason, int Count);

/// <summary>
/// The aggregates, computed by the SERVER over the WHOLE filtered set.
///
/// ★★ THE SAME FILTER PRODUCES THE ROWS AND THESE NUMBERS. The cards are not a sum of the page the
/// user happens to be looking at, and not a sum the browser computed over an array in memory — both
/// come off the same filtered query, which is the only way "the card matches the table" can be a
/// guarantee rather than a coincidence.
/// </summary>
public sealed record ReconciliationSummaryDto(
    int TotalRows,
    IReadOnlyList<ReconciliationCurrencyTotalDto> ByCurrency,
    IReadOnlyList<ReconciliationReasonCountDto> ByReason);

/// <summary>A page of the queue plus the aggregates for the entire filtered set behind it.</summary>
public sealed record ReconciliationPageDto(
    IReadOnlyList<ReconciliationRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    ReconciliationSummaryDto Summary);

/// <summary>One flat row of the export. ★ Purely tabular — no nesting, no grouping (§ del ticket).</summary>
public sealed record ReconciliationExportRow(
    string Kind,
    string EntityId,
    string ReferenceNumber,
    string PayeeName,
    string PayeeCode,
    string PlanName,
    decimal? Amount,
    string Currency,
    string MoneyKind,
    string PeriodDate,
    DateTimeOffset OccurredAt,
    string Reasons);
