using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IPayRunExcelExportService
{
    byte[] GenerateExcel(IReadOnlyList<PayRunExportRow> rows, string tenantSlug);
}
