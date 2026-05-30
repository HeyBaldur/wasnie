namespace Wasnie.Application.Models.Imports;

public sealed record TransactionImportOptions(bool SkipRowsWithWarnings);

public sealed record TransactionImportPayload(
    Guid TenantId,
    string RequestedByUserId,
    string RequestedByEmail,
    string OriginalFileName,
    TransactionImportColumnMapping ColumnMapping,
    TransactionImportOptions Options,
    List<Dictionary<string, string>> Rows);
