namespace Wasnie.Application.Models.Imports;

public sealed record TransactionUpdateColumnMapping(
    string ReferenceNumberColumn,  // Fixed key — never changeable by the user
    string? AmountColumn,
    string? CurrencyColumn,
    string? TransactionDateColumn,
    string? PayeeCodeColumn,
    string? QuantityColumn = null);

public sealed record TransactionUpdatePayload(
    Guid TenantId,
    string RequestedByUserId,
    string RequestedByEmail,
    string OriginalFileName,
    TransactionUpdateColumnMapping ColumnMapping,
    List<Dictionary<string, string>> Rows);
