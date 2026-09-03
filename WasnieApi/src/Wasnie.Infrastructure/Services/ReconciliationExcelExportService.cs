using ClosedXML.Excel;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;

namespace Wasnie.Infrastructure.Services;

/// <summary>
/// The reconciliation queue as a flat sheet.
///
/// ★ A RECTANGLE, NOT A REPORT. One header row, one row per entry, no merged cells and no grouped
/// sub-headers. The file's job is to be sorted, filtered and pivoted by a person who wants to answer
/// a question this screen did not anticipate, and every decoration makes that harder.
/// </summary>
public sealed class ReconciliationExcelExportService : IReconciliationExcelExportService
{
    private static readonly string[] Headers =
    [
        "Kind",
        "EntityId",
        "ReferenceNumber",
        "PayeeName",
        "PayeeCode",
        "PlanName",
        "Amount",
        "Currency",
        "MoneyKind",
        "PeriodDate",
        "OccurredAt",
        "Reasons",
    ];

    public byte[] GenerateExcel(IReadOnlyList<ReconciliationExportRow> rows, string tenantSlug)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Reconciliation");

        for (var col = 1; col <= Headers.Length; col++)
        {
            var cell = ws.Cell(1, col);
            cell.Value = Headers[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEFA");
        }

        ws.SheetView.FreezeRows(1);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var r = i + 2;

            ws.Cell(r, 1).Value = row.Kind;
            ws.Cell(r, 2).Value = row.EntityId;
            ws.Cell(r, 3).Value = row.ReferenceNumber;
            ws.Cell(r, 4).Value = row.PayeeName;
            ws.Cell(r, 5).Value = row.PayeeCode;
            ws.Cell(r, 6).Value = row.PlanName;

            // ★ A BLANK CELL, NOT A ZERO. Rows that carry no money — a drifted deal, a plan with no
            // live rules — must not read as "0.00 owed": a zero is a measurement and this is its
            // absence. Whoever sums the column in Excel has to get the same total the screen shows.
            var amount = ws.Cell(r, 7);
            if (row.Amount.HasValue)
            {
                amount.Value = row.Amount.Value;
                amount.Style.NumberFormat.Format = "#,##0.00";
            }

            ws.Cell(r, 8).Value = row.Currency;
            ws.Cell(r, 9).Value = row.MoneyKind;
            ws.Cell(r, 10).Value = row.PeriodDate;
            ws.Cell(r, 11).Value = row.OccurredAt.UtcDateTime;
            ws.Cell(r, 11).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            ws.Cell(r, 12).Value = row.Reasons;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
