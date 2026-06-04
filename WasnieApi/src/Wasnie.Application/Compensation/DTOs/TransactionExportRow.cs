namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Flat projection used exclusively for the Excel export.
/// Columns match the spec in WI-PROD-T (see docs/SESSION_LOG.md).
/// </summary>
public sealed record TransactionExportRow(
    Guid Id,
    string ReferenceNumber,
    string? StaffId,
    string? PayeeName,
    decimal Amount,
    string Currency,
    int Quantity,
    DateOnly TransactionDate,
    string Source,
    string Status,
    DateTimeOffset CreatedAt);
