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

    public byte[] GenerateExcel(
        IReadOnlyList<PayoutExportRow> rows,
        IReadOnlyList<PayoutDetailExportRow> detail,
        string tenantSlug)
    {
        using var wb = new XLWorkbook();

        // "Summary", not "Payouts": the file has two sheets now and the pair has to read as one
        // document. It is the same name the single-payout workbook uses, deliberately.
        WritePayeeSummarySheet(wb, rows, "Summary");
        WriteDetailSheet(wb, detail);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// One row per payout: the level somebody raises a payment from.
    ///
    /// ★★ IT IS PUBLIC AND TAKES ITS SHEET NAME BECAUSE TWO WORKBOOKS NEED IT. The payouts export
    /// calls it "Summary" (it is the top level there) and the pay-runs export calls it "Payees" (the
    /// top level there is the runs). A second copy in the other service is a second place to forget
    /// the bold on the amount, and two files that both go to accounting laying out the same data
    /// differently is exactly the inconsistency this avoids.
    /// </summary>
    public static void WritePayeeSummarySheet(
        XLWorkbook wb, IReadOnlyList<PayoutExportRow> rows, string sheetName = "Payees")
    {
        var ws = wb.AddWorksheet(sheetName);

        for (var col = 1; col <= Headers.Length; col++)
        {
            var cell = ws.Cell(1, col);
            cell.Value = Headers[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
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

            var payoutAmountCell = ws.Cell(excelRow, 7);
            payoutAmountCell.Value = row.TotalCommissionAmount;
            payoutAmountCell.Style.NumberFormat.Format = MoneyFormat;
            // The amount that will actually be paid to this person. Bold because it is the one column
            // a reader scans down, and every other number on the row looks just like it.
            payoutAmountCell.Style.Font.Bold = true;

            ws.Cell(excelRow, 8).Value = row.TotalCommissionCurrency;
            ws.Cell(excelRow, 9).Value = row.Status;
            ws.Cell(excelRow, 10).Value = row.CalculatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            ws.Cell(excelRow, 11).Value = row.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        ws.Columns().AdjustToContents();
    }

    private static readonly string[] RunDetailHeaders =
    [
        "PayeeCode",
        "PayeeName",
        "Plan",
        "PeriodStart",
        "PeriodEnd",
        "Invoice",
        "Date",
        "Description",
        "BaseAmount",
        "BaseCurrency",
        "Rule",
        "CommissionAmount",
        "CommissionCurrency",
        "PaymentState",
        "PayoutStatus",
    ];

    private const int RunDetailCommissionCol = 12;

    /// <summary>
    /// What every total on the Summary sheet is made of, one row per commission line.
    ///
    /// ★ THE SHEET IS ALWAYS WRITTEN, even when there is nothing in it. A workbook whose second sheet
    /// appears and disappears depending on the data teaches the reader to check whether it is there,
    /// and one day they will not.
    /// </summary>
    public static void WriteDetailSheet(XLWorkbook wb, IReadOnlyList<PayoutDetailExportRow> detail)
    {
        var ws = wb.AddWorksheet("Detail");

        for (var col = 1; col <= RunDetailHeaders.Length; col++)
        {
            var cell = ws.Cell(1, col);
            cell.Value = RunDetailHeaders[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEFA");
        }

        ws.SheetView.FreezeRows(1);

        for (var i = 0; i < detail.Count; i++)
        {
            var d = detail[i];
            var row = i + 2;

            // The payee repeats on every row: this sheet gets sorted and filtered, and a name written
            // once above a block does not survive the first sort by amount.
            ws.Cell(row, 1).Value = d.PayeeCode;
            ws.Cell(row, 2).Value = d.PayeeName;
            ws.Cell(row, 3).Value = d.PlanName;
            ws.Cell(row, 4).Value = d.PeriodStart.ToString("yyyy-MM-dd");
            ws.Cell(row, 5).Value = d.PeriodEnd.ToString("yyyy-MM-dd");
            ws.Cell(row, 6).Value = d.TransactionReference ?? string.Empty;
            ws.Cell(row, 7).Value = d.TransactionDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ws.Cell(row, 8).Value = d.TransactionDescription ?? string.Empty;

            var baseCell = ws.Cell(row, 9);
            baseCell.Value = d.BaseAmount;
            baseCell.Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 10).Value = d.BaseCurrency;

            ws.Cell(row, 11).Value = d.RuleName;

            var commissionCell = ws.Cell(row, RunDetailCommissionCol);
            commissionCell.Value = d.CommissionAmount;
            commissionCell.Style.NumberFormat.Format = "#,##0.00";
            commissionCell.Style.Font.Bold = true;
            ws.Cell(row, 13).Value = d.CommissionCurrency;

            ws.Cell(row, 14).Value = d.PaymentState;
            ws.Cell(row, 15).Value = d.PayoutStatus;
        }

        ws.Columns().AdjustToContents();
    }

    // ── ONE payout: Summary + Detail ─────────────────────────────────────────────────────────────
    //
    // The list export above answers "which payouts exist". This one answers "what is inside this
    // payout", which is the question somebody reconciling actually has, and until now could only be
    // answered by reading a PDF.
    //
    // TWO SHEETS, NOT ONE. The header is context and the lines are data: a spreadsheet with a preamble
    // above the table cannot be sorted, filtered or pivoted without first deleting the preamble, which
    // is exactly what people do and exactly how a total gets separated from the payout it belongs to.

    private const string MoneyFormat = "#,##0.00";
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E8EEFA");

    private static readonly string[] DetailHeaders =
    [
        "Invoice",
        "Date",
        "Description",
        "BaseAmount",
        "BaseCurrency",
        "Rule",
        "CommissionAmount",
        "CommissionCurrency",
        "PaymentState",
    ];

    public byte[] GenerateSinglePayoutExcel(PayoutDto payout, string tenantSlug)
    {
        using var wb = new XLWorkbook();

        WriteSummarySheet(wb, payout);
        WriteDetailSheet(wb, payout);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteSummarySheet(XLWorkbook wb, PayoutDto payout)
    {
        var ws = wb.AddWorksheet("Summary");

        var fields = new (string Label, string Value)[]
        {
            ("Payee",       payout.PayeeName),
            ("PayeeCode",   payout.PayeeCode ?? string.Empty),
            ("Plan",        payout.PlanName),
            ("PeriodStart", payout.PeriodStart.ToString("yyyy-MM-dd")),
            ("PeriodEnd",   payout.PeriodEnd.ToString("yyyy-MM-dd")),
            ("Status",      payout.Status),
            ("CalculatedAt", payout.CalculatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ("CalculatedBy", payout.CalculatedBy),
            ("Lines",       payout.Lines.Count.ToString()),
        };

        for (var i = 0; i < fields.Length; i++)
        {
            var row = i + 1;
            var label = ws.Cell(row, 1);
            label.Value = fields[i].Label;
            label.Style.Font.Bold = true;
            label.Style.Fill.BackgroundColor = HeaderFill;
            ws.Cell(row, 2).Value = fields[i].Value;
        }

        // The total goes last and in bold: it is the figure the whole file exists to state, and the one
        // a reader checks first.
        var totalRow = fields.Length + 2;
        var totalLabel = ws.Cell(totalRow, 1);
        totalLabel.Value = "TotalCommission";
        totalLabel.Style.Font.Bold = true;
        totalLabel.Style.Fill.BackgroundColor = HeaderFill;

        var totalCell = ws.Cell(totalRow, 2);
        totalCell.Value = payout.TotalCommissionAmount;
        totalCell.Style.NumberFormat.Format = MoneyFormat;
        totalCell.Style.Font.Bold = true;
        ws.Cell(totalRow, 3).Value = payout.TotalCommissionCurrency;

        ws.Columns().AdjustToContents();
    }

    private static void WriteDetailSheet(XLWorkbook wb, PayoutDto payout)
    {
        var ws = wb.AddWorksheet("Detail");

        for (var col = 1; col <= DetailHeaders.Length; col++)
        {
            var cell = ws.Cell(1, col);
            cell.Value = DetailHeaders[col - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
        }

        ws.SheetView.FreezeRows(1);

        for (var i = 0; i < payout.Lines.Count; i++)
        {
            var line = payout.Lines[i];
            var row = i + 2;

            ws.Cell(row, 1).Value = line.TransactionReference ?? string.Empty;
            ws.Cell(row, 2).Value = line.TransactionDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            ws.Cell(row, 3).Value = line.TransactionDescription ?? string.Empty;

            var baseCell = ws.Cell(row, 4);
            baseCell.Value = line.BaseAmount;
            baseCell.Style.NumberFormat.Format = MoneyFormat;
            ws.Cell(row, 5).Value = line.BaseCurrency;

            ws.Cell(row, 6).Value = line.RuleName;

            // ★ THE COLUMN THE FILE IS OPENED FOR. Bold on every row, not only on the total: this is
            //   the figure being reconciled, and it has to be findable at a glance in a sheet whose
            //   other numbers look just like it.
            var commissionCell = ws.Cell(row, 7);
            commissionCell.Value = line.CommissionAmount;
            commissionCell.Style.NumberFormat.Format = MoneyFormat;
            commissionCell.Style.Font.Bold = true;
            ws.Cell(row, 8).Value = line.CommissionCurrency;

            // ★★ THE COLUMN THAT PREVENTS PAYING TWICE. A line can be Unpaid, paid by THIS payout, or
            //    already paid by ANOTHER one. Without it a reader summing this sheet against a bank
            //    statement has no way to see that some of the money already left through a different
            //    payout — which is the duplicate the discard flow exists for.
            ws.Cell(row, 9).Value = line.PaymentState.ToString();
        }

        // The total again, under its own column, so the sheet adds up on its own without the reader
        // going back to Summary to check.
        if (payout.Lines.Count > 0)
        {
            var totalRow = payout.Lines.Count + 2;
            var label = ws.Cell(totalRow, 6);
            label.Value = "TOTAL";
            label.Style.Font.Bold = true;

            var total = ws.Cell(totalRow, 7);
            total.Value = payout.TotalCommissionAmount;
            total.Style.NumberFormat.Format = MoneyFormat;
            total.Style.Font.Bold = true;
            total.Style.Border.TopBorder = XLBorderStyleValues.Thin;

            ws.Cell(totalRow, 8).Value = payout.TotalCommissionCurrency;
        }

        ws.Columns().AdjustToContents();
    }
}
