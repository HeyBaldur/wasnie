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

        var result = await Sut.ParseAsync(stream, "valid_10_payees.csv");

        result.Headers.Should().BeEquivalentTo(
            new[] { "FullName", "EmployeeCode", "Email", "HireDate" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task ParseCsv_Valid10Rows_Returns10Rows()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "valid_10_payees.csv"));

        var result = await Sut.ParseAsync(stream, "valid_10_payees.csv");

        result.Rows.Should().HaveCount(10);
    }

    [Fact]
    public async Task ParseCsv_EmptyFile_ReturnsZeroRows()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "empty_file.csv"));

        var result = await Sut.ParseAsync(stream, "empty_file.csv");

        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseCsv_TooManyRows_ThrowsInvalidOperation()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "too_many_rows.csv"));

        var act = async () => await Sut.ParseAsync(stream, "too_many_rows.csv");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*300*");
    }

    [Fact]
    public async Task ParseCsv_CommaInQuotedField_ParsesCorrectly()
    {
        const string csv = "FullName,EmployeeCode,Email,HireDate\n\"Smith, Jr.\",EMP001,smith@corp.com,2021-06-01\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await Sut.ParseAsync(stream, "test.csv");

        result.Rows.Should().HaveCount(1);
        result.Rows[0]["FullName"].Should().Be("Smith, Jr.");
    }

    [Fact]
    public async Task ParseCsv_SpecialCharsInName_PreservedExactly()
    {
        const string csv = "FullName,EmployeeCode,Email,HireDate\nGarcía Ñoño,EMP001,garcia@corp.com,2021-06-01\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await Sut.ParseAsync(stream, "test.csv");

        result.Rows[0]["FullName"].Should().Be("García Ñoño");
    }

    // ──────────────────────────────────────────────────────────
    //  XLSX
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseXlsx_Valid10Rows_ReturnsCorrectHeaders()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "valid_10_payees.xlsx"));

        var result = await Sut.ParseAsync(stream, "valid_10_payees.xlsx");

        result.Headers.Should().BeEquivalentTo(
            new[] { "FullName", "EmployeeCode", "Email", "HireDate" },
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task ParseXlsx_Valid10Rows_Returns10Rows()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "valid_10_payees.xlsx"));

        var result = await Sut.ParseAsync(stream, "valid_10_payees.xlsx");

        result.Rows.Should().HaveCount(10);
    }

    [Fact]
    public async Task ParseXlsx_EmptyFile_ThrowsInvalidOperation()
    {
        // Build an XLSX with NO rows at all (not even a header row)
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("Sheet1");
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var act = async () => await Sut.ParseAsync(ms, "empty.xlsx");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task ParseXlsx_HeadersOnlyFile_ReturnsZeroRows()
    {
        // Build an XLSX with only the header row and no data rows
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

        var result = await Sut.ParseAsync(ms, "headers_only.xlsx");

        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseXlsx_SpecialChars_PreservesAccents()
    {
        await using var stream = File.OpenRead(Path.Combine(FixtureDir, "special_chars.xlsx"));

        var result = await Sut.ParseAsync(stream, "special_chars.xlsx");

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

        var act = async () => await Sut.ParseAsync(stream, $"file{ext}");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
