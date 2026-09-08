using ClosedXML.Excel;
using FluentAssertions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Services;

/// <summary>
/// The accounting workbook: Summary (one row per payee) + Detail (one row per commission line).
///
/// ★★ THIS IS THE FILE SOMEBODY PAYS FROM. It leaves the product and a person raises a transfer from
/// it. A line missing here is a person underpaid; a payment state missing here is a commission paid
/// twice. Every assertion below is about one of those two outcomes.
/// </summary>
public sealed class PayoutRunExcelDetailTests
{
    private const int SummaryAmountCol = 7;
    private const int DetailPayeeCodeCol = 1;
    private const int DetailPayeeNameCol = 2;
    private const int DetailInvoiceCol = 6;
    private const int DetailBaseCol = 9;
    private const int DetailCommissionCol = 12;
    private const int DetailPaymentStateCol = 14;

    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static PayoutExportRow Summary(string code = "EMP-1", decimal total = 600m) =>
        new(
            Id: Guid.NewGuid(),
            PayeeName: "Payee " + code,
            PayeeCode: code,
            PlanName: "EU Accelerator",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            TotalCommissionAmount: total,
            TotalCommissionCurrency: "EUR",
            Status: "Approved",
            CalculatedAt: Now,
            UpdatedAt: Now);

    private static PayoutDetailExportRow Detail(
        string code = "EMP-1",
        string? invoice = "INV-1042",
        decimal commission = 600m,
        string paymentState = "Unpaid") =>
        new(
            PayoutId: Guid.NewGuid(),
            PayeeName: "Payee " + code,
            PayeeCode: code,
            PlanName: "EU Accelerator",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            TransactionReference: invoice,
            TransactionDate: new DateOnly(2026, 6, 24),
            TransactionDescription: "Acme renewal",
            BaseAmount: 12_000m,
            BaseCurrency: "EUR",
            RuleName: "Base Commission",
            CommissionAmount: commission,
            CommissionCurrency: "EUR",
            PaymentState: paymentState,
            PayoutStatus: "Approved");

    private static XLWorkbook Build(
        IReadOnlyList<PayoutExportRow> rows, IReadOnlyList<PayoutDetailExportRow> detail)
    {
        var bytes = new PayoutExcelExportService().GenerateExcel(rows, detail, "acme");
        return new XLWorkbook(new MemoryStream(bytes));
    }

    // ── the shape ─────────────────────────────────────────────────────────────

    [Fact]
    public void ItProducesSummaryThenDetail()
    {
        using var wb = Build([Summary()], [Detail()]);

        wb.Worksheets.Select(w => w.Name).Should().Equal("Summary", "Detail");
    }

    [Fact]
    public void TheDetailSheetExistsEvenWhenThereIsNothingInIt()
    {
        // A second sheet that comes and goes teaches the reader to check whether it is there, and one
        // day they will not.
        using var wb = Build([Summary()], []);
        var ws = wb.Worksheet("Detail");

        ws.Cell(1, DetailPayeeCodeCol).GetString().Should().Be("PayeeCode");
        ws.Cell(2, DetailPayeeCodeCol).GetString().Should().BeEmpty();
    }

    // ── what accounting pays from ─────────────────────────────────────────────

    [Fact]
    public void TheSummaryStillCarriesEveryPayeeItAlwaysDid()
    {
        // The Summary is what this export already produced. Adding the Detail sheet must not change
        // one cell of it: somebody is already paying from this file.
        var rows = new[] { Summary("EMP-1", 600m), Summary("EMP-2", 2_000m) };
        using var wb = Build(rows, []);
        var ws = wb.Worksheet("Summary");

        ws.Cell(2, 3).GetString().Should().Be("EMP-1");
        ws.Cell(2, SummaryAmountCol).GetValue<decimal>().Should().Be(600m);
        ws.Cell(3, 3).GetString().Should().Be("EMP-2");
        ws.Cell(3, SummaryAmountCol).GetValue<decimal>().Should().Be(2_000m);
    }

    [Fact]
    public void TheAmountToPayIsBoldOnTheSummary()
    {
        using var wb = Build([Summary()], []);

        wb.Worksheet("Summary").Cell(2, SummaryAmountCol).Style.Font.Bold.Should().BeTrue();
    }

    [Fact]
    public void TheCommissionColumnIsBoldOnEveryDetailRow()
    {
        using var wb = Build([Summary()], [Detail(), Detail("EMP-2")]);
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, DetailCommissionCol).Style.Font.Bold.Should().BeTrue();
        ws.Cell(3, DetailCommissionCol).Style.Font.Bold.Should().BeTrue();
        ws.Cell(2, DetailBaseCol).Style.Font.Bold.Should().BeFalse();
    }

    [Fact]
    public void AmountsAreNumbersSoTheSheetCanBeSummed()
    {
        // A commission written as text looks identical and refuses to add up, which turns the one
        // thing a spreadsheet is for into manual re-typing.
        using var wb = Build([Summary()], [Detail()]);
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, DetailCommissionCol).DataType.Should().Be(XLDataType.Number);
        ws.Cell(2, DetailBaseCol).DataType.Should().Be(XLDataType.Number);
        wb.Worksheet("Summary").Cell(2, SummaryAmountCol).DataType.Should().Be(XLDataType.Number);
    }

    // ── the two failures this file can cause ──────────────────────────────────

    [Fact]
    public void EveryDetailRowNamesItsOwnPayee()
    {
        // ★★ THE ROW MUST SURVIVE A SORT. A payee written once above a block of rows is reassigned to
        //    the wrong person the first time somebody sorts by amount — and this file is sorted.
        var detail = new[]
        {
            Detail("EMP-1", "INV-1042", 600m),
            Detail("EMP-2", "INV-1043", 2_000m),
        };
        using var wb = Build([Summary("EMP-1"), Summary("EMP-2")], detail);
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, DetailPayeeCodeCol).GetString().Should().Be("EMP-1");
        ws.Cell(2, DetailPayeeNameCol).GetString().Should().Be("Payee EMP-1");
        ws.Cell(3, DetailPayeeCodeCol).GetString().Should().Be("EMP-2");
        ws.Cell(3, DetailPayeeNameCol).GetString().Should().Be("Payee EMP-2");
    }

    [Theory]
    [InlineData("Unpaid")]
    [InlineData("PaidByThisPayout")]
    [InlineData("PaidByAnotherPayout")]
    public void EveryDetailRowSaysWhetherItsMoneyAlreadyLeft(string state)
    {
        // ★★ WITHOUT THIS COLUMN THE FILE CAN BE PAID TWICE. A line already settled by another payout
        //    is indistinguishable from an outstanding one.
        using var wb = Build([Summary()], [Detail(paymentState: state)]);

        wb.Worksheet("Detail").Cell(2, DetailPaymentStateCol).GetString().Should().Be(state);
    }

    [Fact]
    public void TheDetailAddsUpToTheSummaryForEachPayee()
    {
        // ★★ THE INVARIANT OF THE FILE. If the lines and the total disagree, accounting has two numbers
        //    from us and no way to know which one to pay.
        var detail = new[]
        {
            Detail("EMP-1", "INV-1042", 600m),
            Detail("EMP-1", "INV-1043", 400m),
        };
        using var wb = Build([Summary("EMP-1", total: 1_000m)], detail);
        var ws = wb.Worksheet("Detail");

        var sum = Enumerable.Range(2, detail.Length)
            .Sum(r => ws.Cell(r, DetailCommissionCol).GetValue<decimal>());

        sum.Should().Be(wb.Worksheet("Summary").Cell(2, SummaryAmountCol).GetValue<decimal>());
    }

    [Fact]
    public void ALineWithNoInvoiceKeepsItsRow()
    {
        // The reference is nullable. Dropping such a line would take money out of a sheet whose
        // Summary still counts it, and the two would stop agreeing (§B1).
        using var wb = Build([Summary()], [Detail(invoice: null)]);
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, DetailInvoiceCol).GetString().Should().BeEmpty();
        ws.Cell(2, DetailCommissionCol).GetValue<decimal>().Should().Be(600m);
    }

    [Fact]
    public void TheHeaderRowIsFrozenOnBothSheets()
    {
        using var wb = Build([Summary()], [Detail()]);

        wb.Worksheet("Summary").SheetView.SplitRow.Should().Be(1);
        wb.Worksheet("Detail").SheetView.SplitRow.Should().Be(1);
    }
}
