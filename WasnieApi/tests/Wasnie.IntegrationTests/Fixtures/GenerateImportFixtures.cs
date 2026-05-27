using System.Text;
using ClosedXML.Excel;

namespace Wasnie.IntegrationTests.Fixtures;

/// <summary>
/// Generates fixture import files used by FileParserServiceTests and PayeeImportEndpointsTests.
/// Call EnsureCreated once; subsequent calls are no-ops if files already exist.
/// </summary>
internal static class GenerateImportFixtures
{
    // Default column names used across all CSV/XLSX fixtures
    internal const string ColFullName = "FullName";
    internal const string ColCode = "EmployeeCode";
    internal const string ColEmail = "Email";
    internal const string ColHireDate = "HireDate";
    internal const string ColManagerCode = "ManagerCode";

    public static void EnsureCreated(string directory)
    {
        Directory.CreateDirectory(directory);

        GenerateValid10PayeesCsv(directory);
        GenerateValidWithWarningsCsv(directory);
        GenerateValidWithErrorsCsv(directory);
        GenerateCrossRowManagersCsv(directory);
        GenerateEmptyFileCsv(directory);
        GenerateTooManyRowsCsv(directory);
        GenerateValid10PayeesXlsx(directory);
        GenerateSpecialCharsXlsx(directory);
    }

    // ──────────────────────────────────────────────────────────
    //  CSV helpers
    // ──────────────────────────────────────────────────────────

    private static void GenerateValid10PayeesCsv(string dir)
    {
        var path = Path.Combine(dir, "valid_10_payees.csv");
        if (File.Exists(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{ColFullName},{ColCode},{ColEmail},{ColHireDate}");
        for (var i = 1; i <= 10; i++)
        {
            var year = 2020 + ((i - 1) % 4);
            sb.AppendLine($"Payee {i:D2},EMP{i:D3},payee{i:D2}@company.com,{year}-0{(i % 9) + 1}-15");
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateValidWithWarningsCsv(string dir)
    {
        var path = Path.Combine(dir, "valid_with_warnings.csv");
        if (File.Exists(path)) return;

        var recent = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd");
        var sb = new StringBuilder();
        sb.AppendLine($"{ColFullName},{ColCode},{ColEmail},{ColHireDate}");
        // 3 rows with recent hire dates (warning)
        for (var i = 1; i <= 3; i++)
            sb.AppendLine($"Recent {i},REMP{i:D3},recent{i}@company.com,{recent}");
        // 2 clean rows
        sb.AppendLine("Clean One,CEMP001,clean1@company.com,2022-06-01");
        sb.AppendLine("Clean Two,CEMP002,clean2@company.com,2021-03-15");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateValidWithErrorsCsv(string dir)
    {
        var path = Path.Combine(dir, "valid_with_errors.csv");
        if (File.Exists(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{ColFullName},{ColCode},{ColEmail},{ColHireDate}");
        // 3 rows with missing email (error)
        for (var i = 1; i <= 3; i++)
            sb.AppendLine($"Error Row {i},EERR{i:D3},,2021-01-01");
        // 2 valid rows
        sb.AppendLine("Valid One,EVAL001,valid1@company.com,2022-05-10");
        sb.AppendLine("Valid Two,EVAL002,valid2@company.com,2020-11-20");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateCrossRowManagersCsv(string dir)
    {
        var path = Path.Combine(dir, "cross_row_managers.csv");
        if (File.Exists(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{ColFullName},{ColCode},{ColEmail},{ColHireDate},{ColManagerCode}");
        // Row 1: the manager
        sb.AppendLine("The Manager,MGR001,manager@company.com,2018-01-10,");
        // Rows 2-3: reference MGR001 as their manager
        sb.AppendLine("Report One,REP001,report1@company.com,2020-03-01,MGR001");
        sb.AppendLine("Report Two,REP002,report2@company.com,2021-07-15,MGR001");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateEmptyFileCsv(string dir)
    {
        var path = Path.Combine(dir, "empty_file.csv");
        if (File.Exists(path)) return;
        // Headers only, zero data rows
        File.WriteAllText(path, $"{ColFullName},{ColCode},{ColEmail},{ColHireDate}\n", Encoding.UTF8);
    }

    private static void GenerateTooManyRowsCsv(string dir)
    {
        var path = Path.Combine(dir, "too_many_rows.csv");
        if (File.Exists(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{ColFullName},{ColCode},{ColEmail},{ColHireDate}");
        for (var i = 1; i <= 301; i++)
            sb.AppendLine($"Bulk Payee {i},BULK{i:D4},bulk{i}@company.com,2020-01-01");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    // ──────────────────────────────────────────────────────────
    //  XLSX helpers
    // ──────────────────────────────────────────────────────────

    private static void GenerateValid10PayeesXlsx(string dir)
    {
        var path = Path.Combine(dir, "valid_10_payees.xlsx");
        if (File.Exists(path)) return;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Payees");
        ws.Cell(1, 1).Value = ColFullName;
        ws.Cell(1, 2).Value = ColCode;
        ws.Cell(1, 3).Value = ColEmail;
        ws.Cell(1, 4).Value = ColHireDate;

        for (var i = 1; i <= 10; i++)
        {
            var year = 2020 + ((i - 1) % 4);
            ws.Cell(i + 1, 1).Value = $"Payee {i:D2}";
            ws.Cell(i + 1, 2).Value = $"EMP{i:D3}";
            ws.Cell(i + 1, 3).Value = $"payee{i:D2}@company.com";
            ws.Cell(i + 1, 4).Value = $"{year}-0{(i % 9) + 1}-15";
        }
        wb.SaveAs(path);
    }

    private static void GenerateSpecialCharsXlsx(string dir)
    {
        var path = Path.Combine(dir, "special_chars.xlsx");
        if (File.Exists(path)) return;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Payees");
        ws.Cell(1, 1).Value = ColFullName;
        ws.Cell(1, 2).Value = ColCode;
        ws.Cell(1, 3).Value = ColEmail;
        ws.Cell(1, 4).Value = ColHireDate;

        var specialNames = new[]
        {
            "García-López",
            "O'Brien",
            "Müller Ñoño",
        };

        for (var i = 0; i < specialNames.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = specialNames[i];
            ws.Cell(i + 2, 2).Value = $"SPEC{i + 1:D3}";
            ws.Cell(i + 2, 3).Value = $"special{i + 1}@company.com";
            ws.Cell(i + 2, 4).Value = "2022-04-20";
        }
        wb.SaveAs(path);
    }
}
