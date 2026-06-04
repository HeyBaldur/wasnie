using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface ICreditExcelExportService
{
    byte[] GenerateExcel(IReadOnlyList<CreditExportRow> rows, string tenantSlug);
}
