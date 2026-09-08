using ClosedXML.Excel;
using FluentAssertions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Services;

/// <summary>
/// The pay-runs workbook: Runs + Payees + Detail.
///
/// ★★ THE BUG THESE PIN. Filtering /pay-runs to Paid and exporting produced four rows — one per run —
/// that named nobody. The file said four runs were paid, for how much, and who pressed the button; it
/// could not say WHO had been paid. Two sheets down is the answer, and it has to stay there.
/// </summary>
public sealed class PayRunExcelThreeSheetTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private const int PayeeCodeCol = 3;
    private const int PayeeAmountCol = 7;
    private const int DetailPayeeCodeCol = 1;
    private const int DetailCommissionCol = 12;
    private const int DetailPaymentStateCol = 14;

    private static PayRunExportRow Run(string status = "Paid") =>
        new(
            Id: Guid.NewGuid(),
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            Status: status,
            PayeeCount: 2,
            PaidPayeeCount: 2,
            ZeroPayoutCount: 0,
            CreatedBy: "admin@test.com",
            CreatedAt: Now,
            ApprovedBy: "admin@test.com",
            ApprovedAt: Now,
            PaidBy: "admin@test.com",
            PaidAt: Now,
            TotalAmounts: new Dictionary<string, decimal> { ["EUR"] = 2_600m });

    private static PayoutExportRow Payee(string code, decimal total) =>
        new(
            Id: Guid.NewGuid(),
            PayeeName: "Payee " + code,
            PayeeCode: code,
            PlanName: "EU Accelerator",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            TotalCommissionAmount: total,
            TotalCommissionCurrency: "EUR",
            Status: "Paid",
            CalculatedAt: Now,
            UpdatedAt: Now);

    private static PayoutDetailExportRow Line(string code, decimal commission, string state = "PaidByThisPayout") =>
        new(
            PayoutId: Guid.NewGuid(),
            PayeeName: "Payee " + code,
            PayeeCode: code,
            PlanName: "EU Accelerator",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            TransactionReference: "INV-1042",
            TransactionDate: new DateOnly(2026, 6, 24),
            TransactionDescription: "Acme renewal",
            BaseAmount: 12_000m,
            BaseCurrency: "EUR",
            RuleName: "Base Commission",
            CommissionAmount: commission,
            CommissionCurrency: "EUR",
            PaymentState: state,
            PayoutStatus: "Paid");

    private static XLWorkbook Build(
        IReadOnlyList<PayRunExportRow> runs,
        IReadOnlyList<PayoutExportRow> payees,
        IReadOnlyList<PayoutDetailExportRow> detail)
    {
        var bytes = new PayRunExcelExportService().GenerateExcel(runs, payees, detail, "acme");
        return new XLWorkbook(new MemoryStream(bytes));
    }

    [Fact]
    public void ItProducesRunsThenPayeesThenDetail()
    {
        using var wb = Build([Run()], [Payee("EMP-1", 600m)], [Line("EMP-1", 600m)]);

        wb.Worksheets.Select(w => w.Name).Should().Equal("Runs", "Payees", "Detail");
    }

    [Fact]
    public void TheRunsSheetStillSaysWhoPaidAndWhen()
    {
        // It was never wrong, only incomplete. The audit columns must survive the change.
        using var wb = Build([Run()], [], []);
        var text = string.Join("|", wb.Worksheet("Runs").Row(2).CellsUsed().Select(c => c.GetString()));

        text.Should().Contain("Paid");
        text.Should().Contain("admin@test.com");
        text.Should().Contain("2026-07-01");
    }

    /// <summary>
    /// ★★ THE ACTUAL COMPLAINT: "no sé a quién le pagué". The exported file must name every payee.
    /// </summary>
    [Fact]
    public void ThePayeesSheetNamesEveryPersonPaid()
    {
        var payees = new[] { Payee("EMP-1", 600m), Payee("EMP-2", 2_000m) };
        using var wb = Build([Run()], payees, []);
        var ws = wb.Worksheet("Payees");

        ws.Cell(2, PayeeCodeCol).GetString().Should().Be("EMP-1");
        ws.Cell(2, PayeeAmountCol).GetValue<decimal>().Should().Be(600m);
        ws.Cell(3, PayeeCodeCol).GetString().Should().Be("EMP-2");
        ws.Cell(3, PayeeAmountCol).GetValue<decimal>().Should().Be(2_000m);
    }

    [Fact]
    public void TheDetailSheetSaysWhichSalesTheMoneyCameFrom()
    {
        using var wb = Build([Run()], [Payee("EMP-1", 600m)], [Line("EMP-1", 600m)]);
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, DetailPayeeCodeCol).GetString().Should().Be("EMP-1");
        ws.Cell(2, 6).GetString().Should().Be("INV-1042");
        ws.Cell(2, DetailCommissionCol).GetValue<decimal>().Should().Be(600m);
        ws.Cell(2, DetailPaymentStateCol).GetString().Should().Be("PaidByThisPayout");
    }

    [Fact]
    public void TheAmountColumnsAreBoldAndNumericOnBothLowerSheets()
    {
        using var wb = Build([Run()], [Payee("EMP-1", 600m)], [Line("EMP-1", 600m)]);

        var payees = wb.Worksheet("Payees").Cell(2, PayeeAmountCol);
        payees.Style.Font.Bold.Should().BeTrue();
        payees.DataType.Should().Be(XLDataType.Number);

        var detail = wb.Worksheet("Detail").Cell(2, DetailCommissionCol);
        detail.Style.Font.Bold.Should().BeTrue();
        detail.DataType.Should().Be(XLDataType.Number);
    }

    [Fact]
    public void ThePayeeTotalsAddUpToTheRunTotal()
    {
        // ★★ If the sheets disagree, the file states two different amounts for the same run and nobody
        //    can say which one was paid.
        var payees = new[] { Payee("EMP-1", 600m), Payee("EMP-2", 2_000m) };
        using var wb = Build([Run()], payees, []);
        var ws = wb.Worksheet("Payees");

        var sum = Enumerable.Range(2, payees.Length).Sum(r => ws.Cell(r, PayeeAmountCol).GetValue<decimal>());

        // The run row carries 2,600 in its EUR column (col 14 — the first dynamic currency column).
        sum.Should().Be(wb.Worksheet("Runs").Cell(2, 14).GetValue<decimal>());
    }

    [Fact]
    public void TheLowerSheetsExistEvenWhenTheRunsHaveNoPayouts()
    {
        // A sheet that appears and disappears teaches the reader to check whether it is there.
        using var wb = Build([Run(status: "Draft")], [], []);

        wb.Worksheets.Select(w => w.Name).Should().Equal("Runs", "Payees", "Detail");
        wb.Worksheet("Payees").Cell(1, PayeeCodeCol).GetString().Should().Be("PayeeCode");
        wb.Worksheet("Detail").Cell(1, DetailPayeeCodeCol).GetString().Should().Be("PayeeCode");
    }
}
