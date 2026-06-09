using ClosedXML.Excel;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Infrastructure.Services;

public sealed class PayoutExcelExportService : IPayoutExcelExportService
{
    private static readonly string[] Headers =
    [
        "Id",
        "PayeeName",
        "PayeeCode",
        "PlanName",
        "PeriodStart",
        "PeriodEnd",
        "TotalCommissionAmount",
        "TotalCommissionCurrency",
        "Status",
        "CalculatedAt",
        "UpdatedAt",
    ];

    public byte[] GenerateExcel(IReadOnlyList<PayoutExportRow> rows, string tenantSlug)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Payouts");

        for (var col = 1; col <= Headers.Length; col++)
        {
            var cell = ws.Cell(1, col);
            cell.Value = Headers[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEFA");
        }

        ws.SheetView.FreezeRows(1);

        for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var row = rows[rowIdx];
            var excelRow = rowIdx + 2;

            ws.Cell(excelRow, 1).Value = row.Id.ToString();
            ws.Cell(excelRow, 2).Value = row.PayeeName;
            ws.Cell(excelRow, 3).Value = row.PayeeCode ?? string.Empty;
            ws.Cell(excelRow, 4).Value = row.PlanName;
            ws.Cell(excelRow, 5).Value = row.PeriodStart.ToString("yyyy-MM-dd");
            ws.Cell(excelRow, 6).Value = row.PeriodEnd.ToString("yyyy-MM-dd");
            ws.Cell(excelRow, 7).Value = row.TotalCommissionAmount;
            ws.Cell(excelRow, 8).Value = row.TotalCommissionCurrency;
            ws.Cell(excelRow, 9).Value = row.Status;
            ws.Cell(excelRow, 10).Value = row.CalculatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            ws.Cell(excelRow, 11).Value = row.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
