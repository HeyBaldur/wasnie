using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface ITransactionExcelExportService
{
    /// <summary>
    /// Generates an in-memory .xlsx file from the provided transaction rows.
    /// </summary>
    byte[] GenerateExcel(IReadOnlyList<TransactionExportRow> rows, string tenantSlug);
}
