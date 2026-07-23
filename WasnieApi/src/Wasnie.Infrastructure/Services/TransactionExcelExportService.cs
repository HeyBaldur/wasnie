using ClosedXML.Excel;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Infrastructure.Services;

public sealed class TransactionExcelExportService : ITransactionExcelExportService
{
    private static readonly string[] Headers =
    [
        "Id",
        "ReferenceNumber [KEY — DO NOT CHANGE]",
        "Description",
        "StaffId",
        "PayeeName",
        "Amount",
        "Currency",
        "Quantity",
        "TransactionDate",
        "Source",
        "Status [read-only]",
        "CreatedAt [read-only]",
    ];

    public byte[] GenerateExcel(IReadOnlyList<TransactionExportRow> rows, string tenantSlug)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Transactions");

        // Header row
        for (var col = 1; col <= Headers.Length; col++)
        {
            var cell = ws.Cell(1, col);
            cell.Value = Headers[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEFA");
        }

        // Freeze header row
        ws.SheetView.FreezeRows(1);

        // Data rows
        for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var row = rows[rowIdx];
            var excelRow = rowIdx + 2;

            ws.Cell(excelRow, 1).Value = row.Id.ToString();
            ws.Cell(excelRow, 2).Value = row.ReferenceNumber;
            ws.Cell(excelRow, 3).Value = row.Description ?? string.Empty;
            ws.Cell(excelRow, 4).Value = row.StaffId ?? string.Empty;
            ws.Cell(excelRow, 5).Value = row.PayeeName ?? string.Empty;
            var txAmountCell = ws.Cell(excelRow, 6);
            txAmountCell.Value = row.Amount;
            txAmountCell.Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(excelRow, 7).Value = row.Currency;
            ws.Cell(excelRow, 8).Value = row.Quantity;
            ws.Cell(excelRow, 9).Value = row.TransactionDate.ToString("yyyy-MM-dd");
            ws.Cell(excelRow, 10).Value = row.Source;
            ws.Cell(excelRow, 11).Value = row.Status;
            ws.Cell(excelRow, 12).Value = row.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        // Auto-fit columns
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
