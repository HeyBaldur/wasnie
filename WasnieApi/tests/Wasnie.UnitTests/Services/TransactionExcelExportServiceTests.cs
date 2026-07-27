using ClosedXML.Excel;
using FluentAssertions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Services;

/// <summary>
/// Pure unit tests for the transaction Excel export (no DB / no Docker).
/// Covers the two cancellation columns added for the export WI:
/// "Cancellation reason" (col 13) and "Cancelled at" (col 14).
/// </summary>
public sealed class TransactionExcelExportServiceTests
{
    private const int CancellationReasonCol = 13;
    private const int CancelledAtCol = 14;

    private static TransactionExportRow MakeRow(
        string status,
        string? cancelledReason = null,
        DateTimeOffset? cancelledAt = null) =>
        new(
            Id: Guid.NewGuid(),
            ReferenceNumber: "HUBSPOT-507724816618",
            Description: "Deal name",
            StaffId: "EMP-1",
            PayeeName: "Payee One",
            Amount: 1000m,
            Currency: "EUR",
            Quantity: 1,
            TransactionDate: new DateOnly(2026, 6, 20),
            Source: "HubSpot",
            Status: status,
            CreatedAt: new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
            CancelledReason: cancelledReason,
            CancelledAt: cancelledAt);

    private static IXLWorksheet ReadSheet(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var wb = new XLWorkbook(ms);
        return wb.Worksheet(1);
    }

    [Fact]
    public void GenerateExcel_HasCancellationColumnHeaders()
    {
        var service = new TransactionExcelExportService();

        var bytes = service.GenerateExcel([MakeRow("Pending")], "acme");

        var ws = ReadSheet(bytes);
        ws.Cell(1, CancellationReasonCol).GetString().Should().Be("Cancellation reason");
        ws.Cell(1, CancelledAtCol).GetString().Should().Be("Cancelled at");
    }

    [Fact]
    public void GenerateExcel_CancelledTransaction_WritesReasonAndDate()
    {
        var service = new TransactionExcelExportService();
        var cancelledAt = new DateTimeOffset(2026, 6, 23, 11, 42, 16, TimeSpan.Zero);
        var row = MakeRow(
            "Cancelled",
            cancelledReason: "…texto libre escrito por quien canceló…",
            cancelledAt: cancelledAt);

        var bytes = service.GenerateExcel([row], "acme");

        var ws = ReadSheet(bytes);
        // Reason is written literally, unmodified.
        ws.Cell(2, CancellationReasonCol).GetString()
            .Should().Be("…texto libre escrito por quien canceló…");
        // Date uses the same format as the existing CreatedAt column.
        ws.Cell(2, CancelledAtCol).GetString()
            .Should().Be(cancelledAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    [Fact]
    public void GenerateExcel_NonCancelledTransaction_LeavesBothCellsBlank()
    {
        var service = new TransactionExcelExportService();

        var bytes = service.GenerateExcel([MakeRow("Calculated")], "acme");

        var ws = ReadSheet(bytes);
        // Blank cells — never the string "null" nor a dash.
        ws.Cell(2, CancellationReasonCol).GetString().Should().BeEmpty();
        ws.Cell(2, CancelledAtCol).GetString().Should().BeEmpty();
    }
}
