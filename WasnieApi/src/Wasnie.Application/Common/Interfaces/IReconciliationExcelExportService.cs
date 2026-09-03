using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IReconciliationExcelExportService
{
    byte[] GenerateExcel(IReadOnlyList<ReconciliationExportRow> rows, string tenantSlug);
}
