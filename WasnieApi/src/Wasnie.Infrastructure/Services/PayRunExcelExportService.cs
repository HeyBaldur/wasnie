using ClosedXML.Excel;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Infrastructure.Services;

public sealed class PayRunExcelExportService : IPayRunExcelExportService
{
    private static readonly string[] FixedHeaders =
    [
        "Id",
        "PeriodStart",
        "PeriodEnd",
        "Status",
        "PayeeCount",
        "PaidPayeeCount",
        "ZeroPayoutCount",
        "CreatedBy",
        "CreatedAt",
        "ApprovedBy",
        "ApprovedAt",
        "PaidBy",
        "PaidAt",
    ];

    public byte[] GenerateExcel(IReadOnlyList<PayRunExportRow> rows, string tenantSlug)
    {
        // Two-pass: collect distinct currencies for dynamic columns (Pattern B — no cross-currency sum).
        var currencies = rows
            .SelectMany(r => r.TotalAmounts.Keys)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Pay Runs");

        // Write headers.
        var allHeaders = FixedHeaders.Concat(currencies.Select(c => $"Amount_{c}")).ToArray();
        for (var col = 1; col <= allHeaders.Length; col++)
        {
            var cell = ws.Cell(1, col);
            cell.Value = allHeaders[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEFA");
        }
        ws.SheetView.FreezeRows(1);

        // Write data rows.
        for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            var row = rows[rowIdx];
            var excelRow = rowIdx + 2;

            ws.Cell(excelRow, 1).Value  = row.Id.ToString();
            ws.Cell(excelRow, 2).Value  = row.PeriodStart.ToString("yyyy-MM-dd");
            ws.Cell(excelRow, 3).Value  = row.PeriodEnd.ToString("yyyy-MM-dd");
            ws.Cell(excelRow, 4).Value  = row.Status;
            ws.Cell(excelRow, 5).Value  = row.PayeeCount;
            ws.Cell(excelRow, 6).Value  = row.PaidPayeeCount;
            ws.Cell(excelRow, 7).Value  = row.ZeroPayoutCount;
            ws.Cell(excelRow, 8).Value  = row.CreatedBy;
            ws.Cell(excelRow, 9).Value  = row.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            ws.Cell(excelRow, 10).Value = row.ApprovedBy ?? string.Empty;
            ws.Cell(excelRow, 11).Value = row.ApprovedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? string.Empty;
            ws.Cell(excelRow, 12).Value = row.PaidBy ?? string.Empty;
            ws.Cell(excelRow, 13).Value = row.PaidAt?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? string.Empty;

            for (var ci = 0; ci < currencies.Count; ci++)
            {
                if (row.TotalAmounts.TryGetValue(currencies[ci], out var amount))
                    ws.Cell(excelRow, 14 + ci).Value = amount;
                // else: leave cell blank (run has no payouts in this currency).
            }
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
