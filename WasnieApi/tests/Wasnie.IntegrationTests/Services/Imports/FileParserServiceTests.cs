using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Wasnie.Infrastructure.Services.Imports;
using Wasnie.IntegrationTests.Fixtures;

namespace Wasnie.IntegrationTests.Services.Imports;

public sealed class FileParserServiceTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ImportFiles");

    private static readonly FileParserService Sut = new();

    private const int PayeeLimit = 300;
    private const int TxLimit = 10_000;

    static FileParserServiceTests()
    {
        GenerateImportFixtures.EnsureCreated(FixtureDir);
    }

    // ──────────────────────────────────────────────────────────
    //  CSV
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseCsv_Valid10Rows_ReturnsCorrectHeaders()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "valid_10_payees.csv"));

        var result = await Sut.ParseAsync(stream, "valid_10_payees.csv", PayeeLimit);

        result.Headers.Should().BeEquivalentTo(
            new[] { "FullName", "EmployeeCode", "Email", "HireDate" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task ParseCsv_Valid10Rows_Returns10Rows()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "valid_10_payees.csv"));

        var result = await Sut.ParseAsync(stream, "valid_10_payees.csv", PayeeLimit);

        result.Rows.Should().HaveCount(10);
    }

    [Fact]
    public async Task ParseCsv_EmptyFile_ReturnsZeroRows()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "empty_file.csv"));

        var result = await Sut.ParseAsync(stream, "empty_file.csv", PayeeLimit);

        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseCsv_TooManyRows_ThrowsInvalidOperation()
    {
        // too_many_rows.csv has 301 rows — exceeds payee limit of 300
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "too_many_rows.csv"));

        var act = async () => await Sut.ParseAsync(stream, "too_many_rows.csv", PayeeLimit);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*300*");
    }

    [Fact]
    public async Task ParseCsv_ExactlyAtLimit_Succeeds()
    {
        // A file with exactly maxRows=5 rows should succeed
        var csv = BuildCsvRows(5);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await Sut.ParseAsync(stream, "test.csv", 5);

        result.Rows.Should().HaveCount(5);
    }

    [Fact]
    public async Task ParseCsv_OnePastLimit_ThrowsWithLimitInMessage()
    {
        // A file with 6 rows against maxRows=5 must be rejected and include "5" in the message
        var csv = BuildCsvRows(6);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var act = async () => await Sut.ParseAsync(stream, "test.csv", 5);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*5*");
    }

    [Fact]
    public async Task ParseCsv_TransactionLimit10000_AcceptsLargerFiles()
    {
        // Validate parser correctly applies the transaction limit (not the payee limit)
        // A 301-row file should be accepted when maxRows=10000
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "too_many_rows.csv"));

        var result = await Sut.ParseAsync(stream, "too_many_rows.csv", TxLimit);

        result.Rows.Should().HaveCount(301);
    }

    [Fact]
    public async Task ParseCsv_CommaInQuotedField_ParsesCorrectly()
    {
        const string csv = "FullName,EmployeeCode,Email,HireDate\n\"Smith, Jr.\",EMP001,smith@corp.com,2021-06-01\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await Sut.ParseAsync(stream, "test.csv", PayeeLimit);

        result.Rows.Should().HaveCount(1);
        result.Rows[0]["FullName"].Should().Be("Smith, Jr.");
    }

    [Fact]
    public async Task ParseCsv_SpecialCharsInName_PreservedExactly()
    {
        const string csv = "FullName,EmployeeCode,Email,HireDate\nGarcía Ñoño,EMP001,garcia@corp.com,2021-06-01\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await Sut.ParseAsync(stream, "test.csv", PayeeLimit);

        result.Rows[0]["FullName"].Should().Be("García Ñoño");
    }

    // ──────────────────────────────────────────────────────────
    //  XLSX
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseXlsx_Valid10Rows_ReturnsCorrectHeaders()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "valid_10_payees.xlsx"));

        var result = await Sut.ParseAsync(stream, "valid_10_payees.xlsx", PayeeLimit);

        result.Headers.Should().BeEquivalentTo(
            new[] { "FullName", "EmployeeCode", "Email", "HireDate" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task ParseXlsx_Valid10Rows_Returns10Rows()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "valid_10_payees.xlsx"));

        var result = await Sut.ParseAsync(stream, "valid_10_payees.xlsx", PayeeLimit);

        result.Rows.Should().HaveCount(10);
    }

    [Fact]
    public async Task ParseXlsx_EmptyFile_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("Sheet1");
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var act = async () => await Sut.ParseAsync(ms, "empty.xlsx", PayeeLimit);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task ParseXlsx_HeadersOnlyFile_ReturnsZeroRows()
    {
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "FullName";
            ws.Cell(1, 2).Value = "EmployeeCode";
            ws.Cell(1, 3).Value = "Email";
            ws.Cell(1, 4).Value = "HireDate";
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var result = await Sut.ParseAsync(ms, "headers_only.xlsx", PayeeLimit);

        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseXlsx_ExactlyAtLimit_Succeeds()
    {
        // Build an XLSX with exactly maxRows=5 data rows
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Name";
            for (var i = 1; i <= 5; i++) ws.Cell(i + 1, 1).Value = $"Row {i}";
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var result = await Sut.ParseAsync(ms, "test.xlsx", 5);

        result.Rows.Should().HaveCount(5);
    }

    [Fact]
    public async Task ParseXlsx_OnePastLimit_ThrowsWithLimitInMessage()
    {
        // Build an XLSX with 6 data rows against maxRows=5
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Name";
            for (var i = 1; i <= 6; i++) ws.Cell(i + 1, 1).Value = $"Row {i}";
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var act = async () => await Sut.ParseAsync(ms, "test.xlsx", 5);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*5*");
    }

    [Fact]
    public async Task ParseXlsx_SpecialChars_PreservesAccents()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "special_chars.xlsx"));

        var result = await Sut.ParseAsync(stream, "special_chars.xlsx", PayeeLimit);

        result.Rows.Should().HaveCount(3);
        result.Rows[0]["FullName"].Should().Be("García-López");
        result.Rows[1]["FullName"].Should().Be("O'Brien");
        result.Rows[2]["FullName"].Should().Contain("ñ");
    }

    // ──────────────────────────────────────────────────────────
    //  Format detection
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".txt")]
    [InlineData("")]
    public async Task ParseAsync_UnsupportedExtension_Throws(string ext)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("some data"));

        var act = async () => await Sut.ParseAsync(stream, $"file{ext}", PayeeLimit);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ──────────────────────────────────────────────────────────
    //  XLSX — native cell type preservation (bug fix WI-P2-04a-fix2)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseXlsx_NativeDateTimeCell_ProducesIsoDateString()
    {
        // Smoking-gun regression guard: a native Excel DateTime cell must arrive as
        // "yyyy-MM-dd" — NOT a culture-dependent "4/1/2026 10:21:04 AM" string.
        var bytes = BuildXlsxBytes(new DateTime(2026, 4, 1, 10, 21, 4));
        using var parseStream = new MemoryStream(bytes);

        var result = await Sut.ParseAsync(parseStream, "pos_export.xlsx", TxLimit);

        result.Rows.Should().HaveCount(1);
        result.Rows[0]["Date"].Should().Be("2026-04-01",
            "native Excel DateTime must be formatted as ISO 8601, time component dropped");
    }

    [Fact]
    public async Task ParseXlsx_NativeDateTimeCell_CultureIndependent()
    {
        // With pl-PL culture active, parsing must produce the same ISO result.
        var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture =
            System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            var bytes = BuildXlsxBytes(new DateTime(2026, 4, 1, 10, 21, 4));
            using var parseStream = new MemoryStream(bytes);

            var result = await Sut.ParseAsync(parseStream, "pos_export.xlsx", TxLimit);

            result.Rows[0]["Date"].Should().Be("2026-04-01");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task ParseXlsx_NativeNumberCell_ProducesInvariantDecimalString()
    {
        // A numeric amount cell (e.g. 1500.50) must arrive as "1500.5" (InvariantCulture),
        // not "1 500,5" (pl-PL) or "$1,500.50" (currency format).
        var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture =
            System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            var bytes = BuildXlsxBytesWithNumber(1500.50);
            using var parseStream = new MemoryStream(bytes);

            var result = await Sut.ParseAsync(parseStream, "pos_export.xlsx", TxLimit);

            // Should be parseable as decimal with InvariantCulture
            decimal.TryParse(result.Rows[0]["Amount"],
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed).Should().BeTrue();
            parsed.Should().Be(1500.50m);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static string BuildCsvRows(int dataRowCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Code");
        for (var i = 1; i <= dataRowCount; i++)
            sb.AppendLine($"Row {i},CODE{i:D3}");
        return sb.ToString();
    }

    private static byte[] BuildXlsxBytes(DateTime dateValue)
    {
        using var ms = new MemoryStream();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(2, 1).Value = dateValue;
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static byte[] BuildXlsxBytesWithNumber(double number)
    {
        using var ms = new MemoryStream();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "Amount";
        ws.Cell(2, 1).Value = number;
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
