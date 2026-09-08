using ClosedXML.Excel;
using FluentAssertions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Services;

/// <summary>
/// The Excel of ONE payout: Summary + Detail.
///
/// ★★ WHY IT IS TESTED LIKE MONEY CODE. The file states what a person is owed and gets sent outside
/// the product. A column silently dropped here is not a rendering bug — it is a payee reconciling
/// against a figure that is not the one the system holds. The whole reason this export exists is that
/// the same data already left as a PDF nobody could sum.
/// </summary>
public sealed class PayoutSinglePayoutExcelTests
{
    private const int InvoiceCol = 1;
    private const int DateCol = 2;
    private const int DescriptionCol = 3;
    private const int BaseCol = 4;
    private const int RuleCol = 6;
    private const int CommissionCol = 7;
    private const int CommissionCurrencyCol = 8;
    private const int PaymentStateCol = 9;

    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static PayoutLineDto Line(
        string reference = "INV-1042",
        decimal baseAmount = 12_000m,
        decimal commission = 600m,
        string currency = "EUR",
        PayoutLinePaymentState state = PayoutLinePaymentState.Unpaid) =>
        new(
            Id: Guid.NewGuid(),
            CreditId: Guid.NewGuid(),
            RuleId: Guid.NewGuid(),
            RuleName: "Base Commission",
            BaseAmount: baseAmount,
            BaseCurrency: currency,
            CommissionAmount: commission,
            CommissionCurrency: currency,
            TransactionId: Guid.NewGuid(),
            TransactionReference: reference,
            TransactionDescription: "Acme renewal",
            TransactionExternalId: null,
            TransactionDate: new DateOnly(2026, 6, 24),
            TransactionAmount: baseAmount,
            TransactionCurrency: currency,
            Calculation: null,
            PaymentState: state);

    private static PayoutDto Payout(params PayoutLineDto[] lines) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            PayeeId: Guid.NewGuid(),
            PayeeName: "Rudolph GeHard Chipellin",
            PayeeCode: "EMP-3",
            PlanId: Guid.NewGuid(),
            PlanName: "EU Accelerator Q2 2026",
            PeriodStart: new DateOnly(2026, 7, 1),
            PeriodEnd: new DateOnly(2026, 7, 31),
            TotalCommissionAmount: lines.Sum(l => l.CommissionAmount),
            TotalCommissionCurrency: "EUR",
            Status: "Approved",
            CalculatedAt: Now,
            CalculatedBy: "admin@test.com",
            UpdatedAt: Now,
            UpdatedBy: "admin@test.com",
            Lines: lines);

    private static XLWorkbook Build(PayoutDto payout)
    {
        var bytes = new PayoutExcelExportService().GenerateSinglePayoutExcel(payout, "acme");
        return new XLWorkbook(new MemoryStream(bytes));
    }

    // ── the shape ─────────────────────────────────────────────────────────────

    [Fact]
    public void ItProducesTwoSheetsNamedSummaryAndDetail()
    {
        using var wb = Build(Payout(Line()));

        wb.Worksheets.Select(w => w.Name).Should().Equal("Summary", "Detail");
    }

    [Fact]
    public void TheSummaryCarriesWhoWhatAndWhen()
    {
        // Without these the Detail sheet is a list of numbers belonging to nobody in particular.
        using var wb = Build(Payout(Line()));
        var text = string.Join("|", wb.Worksheet("Summary").CellsUsed().Select(c => c.GetString()));

        text.Should().Contain("Rudolph GeHard Chipellin");
        text.Should().Contain("EMP-3");
        text.Should().Contain("EU Accelerator Q2 2026");
        text.Should().Contain("2026-07-01");
        text.Should().Contain("2026-07-31");
        text.Should().Contain("Approved");
    }

    // ── the money ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryLineIsWrittenWithItsOwnFigures()
    {
        var lines = new[]
        {
            Line("INV-1042", baseAmount: 12_000m, commission: 600m),
            Line("INV-1043", baseAmount: 40_000m, commission: 2_000m),
        };

        using var wb = Build(Payout(lines));
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, InvoiceCol).GetString().Should().Be("INV-1042");
        ws.Cell(2, BaseCol).GetValue<decimal>().Should().Be(12_000m);
        ws.Cell(2, CommissionCol).GetValue<decimal>().Should().Be(600m);

        ws.Cell(3, InvoiceCol).GetString().Should().Be("INV-1043");
        ws.Cell(3, CommissionCol).GetValue<decimal>().Should().Be(2_000m);
    }

    /// <summary>
    /// ★ THE AMOUNTS ARE NUMBERS, NOT TEXT. A commission written as a string looks identical on screen
    ///   and refuses to sum, which turns the one thing a spreadsheet is for into a manual re-typing job.
    /// </summary>
    [Fact]
    public void AmountsAreNumericSoTheSheetCanAddThemUp()
    {
        using var wb = Build(Payout(Line(commission: 600m), Line("INV-1043", commission: 2_000m)));
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, CommissionCol).DataType.Should().Be(XLDataType.Number);
        ws.Cell(3, BaseCol).DataType.Should().Be(XLDataType.Number);
    }

    [Fact]
    public void TheCommissionColumnIsBoldOnEveryRow()
    {
        // It is the column the file is opened for. Bold on the total only would leave the reader
        // scanning a sheet where every number looks the same.
        using var wb = Build(Payout(Line(), Line("INV-1043")));
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, CommissionCol).Style.Font.Bold.Should().BeTrue();
        ws.Cell(3, CommissionCol).Style.Font.Bold.Should().BeTrue();
        ws.Cell(2, BaseCol).Style.Font.Bold.Should().BeFalse();
    }

    [Fact]
    public void TheDetailSheetAddsUpToTheTotalOnTheSummary()
    {
        // ★★ THE INVARIANT OF THE WHOLE FILE. If the rows and the total ever disagree, the payee has
        //    two numbers from us and no way to know which one we will pay.
        var payout = Payout(Line(commission: 600m), Line("INV-1043", commission: 2_000m));
        using var wb = Build(payout);
        var ws = wb.Worksheet("Detail");

        var rowSum = Enumerable.Range(2, payout.Lines.Count)
            .Sum(r => ws.Cell(r, CommissionCol).GetValue<decimal>());

        rowSum.Should().Be(payout.TotalCommissionAmount);

        var totalRow = payout.Lines.Count + 2;
        ws.Cell(totalRow, CommissionCol).GetValue<decimal>().Should().Be(payout.TotalCommissionAmount);
        ws.Cell(totalRow, CommissionCol).Style.Font.Bold.Should().BeTrue();
    }

    [Fact]
    public void TheCurrencyTravelsWithTheAmount()
    {
        // A bare 600 is not an amount. §C4.
        using var wb = Build(Payout(Line(currency: "EUR")));

        wb.Worksheet("Detail").Cell(2, CommissionCurrencyCol).GetString().Should().Be("EUR");
    }

    // ── the reconciliation column ─────────────────────────────────────────────

    [Theory]
    [InlineData(PayoutLinePaymentState.Unpaid, "Unpaid")]
    [InlineData(PayoutLinePaymentState.PaidByThisPayout, "PaidByThisPayout")]
    [InlineData(PayoutLinePaymentState.PaidByAnotherPayout, "PaidByAnotherPayout")]
    public void EachLineSaysWhetherItsMoneyHasAlreadyLeft(PayoutLinePaymentState state, string expected)
    {
        // ★★ THE COLUMN THAT PREVENTS PAYING TWICE. A line already paid by ANOTHER payout looks exactly
        //    like an unpaid one in a sheet without this, and summing the two against a bank statement
        //    is how the same commission gets paid twice.
        using var wb = Build(Payout(Line(state: state)));

        wb.Worksheet("Detail").Cell(2, PaymentStateCol).GetString().Should().Be(expected);
    }

    // ── the edges ─────────────────────────────────────────────────────────────

    [Fact]
    public void APayoutWithNoLinesStillProducesAReadableFile()
    {
        // A zero payout is a real thing and the reader still needs to know whose it is. Throwing here,
        // or emitting a headerless sheet, would turn a legitimate empty statement into a broken file.
        using var wb = Build(Payout());
        var detail = wb.Worksheet("Detail");

        detail.Cell(1, InvoiceCol).GetString().Should().Be("Invoice");
        detail.Cell(2, InvoiceCol).GetString().Should().BeEmpty();
        wb.Worksheet("Summary").CellsUsed().Select(c => c.GetString())
            .Should().Contain("Rudolph GeHard Chipellin");
    }

    [Fact]
    public void ALineWithNoSourceTransactionStillGetsItsRow()
    {
        // The reference is nullable in the DTO. Dropping such a line would remove money from a file
        // whose total still counts it — the rows and the total would stop agreeing (§B1).
        var orphan = Line() with
        {
            TransactionReference = null,
            TransactionDate = null,
            TransactionDescription = null,
        };

        using var wb = Build(Payout(orphan));
        var ws = wb.Worksheet("Detail");

        ws.Cell(2, InvoiceCol).GetString().Should().BeEmpty();
        ws.Cell(2, DateCol).GetString().Should().BeEmpty();
        ws.Cell(2, DescriptionCol).GetString().Should().BeEmpty();
        ws.Cell(2, RuleCol).GetString().Should().Be("Base Commission");
        ws.Cell(2, CommissionCol).GetValue<decimal>().Should().Be(600m);
    }

    [Fact]
    public void TheHeaderRowIsFrozenSoLongPayoutsStayReadable()
    {
        using var wb = Build(Payout(Line()));

        wb.Worksheet("Detail").SheetView.SplitRow.Should().Be(1);
    }
}
