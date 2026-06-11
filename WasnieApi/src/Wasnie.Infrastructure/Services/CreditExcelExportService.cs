using ClosedXML.Excel;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Infrastructure.Services;

public sealed class CreditExcelExportService : ICreditExcelExportService
{
    private static readonly string[] Headers =
    [
        "Id",
        "ReferenceNumber",
        "PayeeName",
        "PayeeCode",
        "PlanName",
        "RuleName",
        "OriginalAmount",
        "OriginalCurrency",
        "CreditedAmount",
        "CreditedCurrency",
        "SplitPercentage",
        "Role",
        "AllocatedAt",
        "AllocatedBy",
        "Status",
        "SupersededAt",
        "SupersededBy",
    ];

    public byte[] GenerateExcel(IReadOnlyList<CreditExportRow> rows, string tenantSlug)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Credits");

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
            ws.Cell(excelRow, 2).Value = row.ReferenceNumber;
            ws.Cell(excelRow, 3).Value = row.PayeeName ?? string.Empty;
            ws.Cell(excelRow, 4).Value = row.PayeeCode ?? string.Empty;
            ws.Cell(excelRow, 5).Value = row.PlanName;
            ws.Cell(excelRow, 6).Value = row.RuleName;
            var origAmtCell = ws.Cell(excelRow, 7);
            origAmtCell.Value = row.OriginalAmount;
            origAmtCell.Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(excelRow, 8).Value = row.OriginalCurrency;
            var creditedAmtCell = ws.Cell(excelRow, 9);
            creditedAmtCell.Value = row.CreditedAmount;
            creditedAmtCell.Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(excelRow, 10).Value = row.CreditedCurrency;
            var splitPctCell = ws.Cell(excelRow, 11);
            splitPctCell.Value = row.SplitPercentage;
            splitPctCell.Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(excelRow, 12).Value = row.Role;
            ws.Cell(excelRow, 13).Value = row.AllocatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            ws.Cell(excelRow, 14).Value = row.AllocatedBy;
            ws.Cell(excelRow, 15).Value = row.Status;
            ws.Cell(excelRow, 16).Value = row.SupersededAt.HasValue
                ? row.SupersededAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")
                : string.Empty;
            ws.Cell(excelRow, 17).Value = row.SupersededBy ?? string.Empty;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
