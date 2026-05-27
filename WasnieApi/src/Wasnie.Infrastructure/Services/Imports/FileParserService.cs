using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class FileParserService : IFileParserService
{
    private const int MaxRows = 300;

    public Task<ParsedFile> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" => Task.FromResult(ParseXlsx(stream)),
            ".csv" => Task.FromResult(ParseCsv(stream)),
            _ => throw new InvalidOperationException($"Unsupported file format '{ext}'. Only .csv and .xlsx are accepted."),
        };
    }

    private static ParsedFile ParseXlsx(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("The file is empty.");

        var headers = rows[0].Cells().Select(c => c.GetString().Trim()).Where(h => h.Length > 0).ToArray();
        if (headers.Length == 0)
            throw new InvalidOperationException("No column headers found in the first row.");

        var dataRows = rows.Skip(1).ToList();
        if (dataRows.Count > MaxRows)
            throw new InvalidOperationException($"The file contains {dataRows.Count} data rows. The maximum allowed is {MaxRows}. Please split the file and import in batches.");

        var result = new List<Dictionary<string, string>>(dataRows.Count);
        foreach (var row in dataRows)
        {
            var dict = new Dictionary<string, string>(headers.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = row.Cell(i + 1);
                dict[headers[i]] = cell.GetString().Trim();
            }
            result.Add(dict);
        }

        return new ParsedFile { Headers = headers, Rows = result, Format = FileFormat.Xlsx };
    }

    private static ParsedFile ParseCsv(Stream stream)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? throw new InvalidOperationException("No header row found in CSV.");
        headers = headers.Select(h => h.Trim()).Where(h => h.Length > 0).ToArray();

        var rows = new List<Dictionary<string, string>>();
        while (csv.Read())
        {
            if (rows.Count >= MaxRows)
                throw new InvalidOperationException($"The file contains more than {MaxRows} data rows. The maximum allowed is {MaxRows}. Please split the file and import in batches.");

            var dict = new Dictionary<string, string>(headers.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
                dict[header] = csv.GetField(header)?.Trim() ?? string.Empty;
            rows.Add(dict);
        }

        return new ParsedFile { Headers = headers, Rows = rows, Format = FileFormat.Csv };
    }
}
