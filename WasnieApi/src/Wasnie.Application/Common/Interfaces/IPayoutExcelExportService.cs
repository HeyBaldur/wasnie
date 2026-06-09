using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Application.Common.Interfaces;

public interface IPayoutExcelExportService
{
    byte[] GenerateExcel(IReadOnlyList<PayoutExportRow> rows, string tenantSlug);
}
