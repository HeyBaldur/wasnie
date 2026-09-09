namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Flat projection used exclusively for the Credits Excel export (WI-PROD-CREDITS-EXPORT).
/// </summary>
public sealed record CreditExportRow(
    Guid Id,
    string ReferenceNumber,
    string? PayeeName,
    string? PayeeCode,
    string PlanName,
    string RuleName,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal CreditedAmount,
    string CreditedCurrency,
    decimal SplitPercentage,
    string Role,
    DateTimeOffset AllocatedAt,
    string AllocatedBy,
    string Status,
    DateTimeOffset? SupersededAt,
    string? SupersededBy,
    /// <summary>
    /// "Paid" | "Unpaid" | "Closed". The column a reader reconciling in a spreadsheet actually needs:
    /// without it, every row said "Active" and the file could not be split into what was paid and what
    /// is still owed.
    /// </summary>
    string Settlement,
    /// <summary>When the money left, for a Paid credit. Null otherwise.</summary>
    DateTimeOffset? PaidAt,
    /// <summary>"WrittenOff" | "ExternalSettlement" for a Closed credit; null otherwise.</summary>
    string? ClosureReason);
