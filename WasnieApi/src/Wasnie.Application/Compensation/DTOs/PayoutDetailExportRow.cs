namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// One commission line of one payout, flattened for the accounting workbook's Detail sheet.
///
/// ★★ THE PAYEE IS REPEATED ON EVERY ROW ON PURPOSE. This sheet is filtered, sorted and pivoted by
/// whoever receives it; a payee written once above a block of rows survives none of those operations,
/// and the first sort by amount silently reassigns every line to the wrong person.
///
/// ★ IT IS NOT <c>PayoutLineDto</c>. That one carries the whole calculation snapshot — rate table,
/// trigger, modifiers — which is what the screen needs to explain ONE line and what would make an
/// export of a 200-payee run load thousands of JSON documents nobody is going to read.
/// </summary>
public sealed record PayoutDetailExportRow(
    Guid PayoutId,
    string PayeeName,
    string PayeeCode,
    string PlanName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? TransactionReference,
    DateOnly? TransactionDate,
    string? TransactionDescription,
    decimal BaseAmount,
    string BaseCurrency,
    string RuleName,
    decimal CommissionAmount,
    string CommissionCurrency,
    /// <summary>
    /// Unpaid / PaidByThisPayout / PaidByAnotherPayout. ★ WITHOUT IT THE SHEET CAN BE PAID TWICE: a
    /// line already settled by a different payout looks exactly like an outstanding one.
    /// </summary>
    string PaymentState,
    string PayoutStatus);
