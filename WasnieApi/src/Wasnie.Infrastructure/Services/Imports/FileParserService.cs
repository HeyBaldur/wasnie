using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class FileParserService : IFileParserService
{
    public Task<ParsedFile> ParseAsync(Stream stream, string fileName, int maxRows, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" => Task.FromResult(ParseXlsx(stream, maxRows)),
            ".csv" => Task.FromResult(ParseCsv(stream, maxRows)),
            _ => throw new InvalidOperationException($"Unsupported file format '{ext}'. Only .csv and .xlsx are accepted."),
        };
    }

    private static ParsedFile ParseXlsx(Stream stream, int maxRows)
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
        if (dataRows.Count > maxRows)
            throw new InvalidOperationException($"The file contains {dataRows.Count} data rows. The maximum allowed is {maxRows}. Please split the file and import in batches.");

        var result = new List<Dictionary<string, string>>(dataRows.Count);
        foreach (var row in dataRows)
        {
            var dict = new Dictionary<string, string>(headers.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = row.Cell(i + 1);
                dict[headers[i]] = ReadCellAsString(cell);
            }
            result.Add(dict);
        }

        return new ParsedFile { Headers = headers, Rows = result, Format = FileFormat.Xlsx };
    }

    // Preserve native Excel cell types instead of using culture-dependent GetString().
    // DateTime cells → ISO 8601 "yyyy-MM-dd" (drops time component; POS exports have HH:mm:ss).
    // Number cells  → invariant decimal string (avoids currency/thousand-separator formatting).
    // All others    → GetString() trimmed (text, blank, boolean, error).
    private static string ReadCellAsString(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var d))
            return d.ToString(CultureInfo.InvariantCulture);

        return cell.GetString().Trim();
    }

    private static ParsedFile ParseCsv(Stream stream, int maxRows)
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
            if (rows.Count >= maxRows)
                throw new InvalidOperationException($"The file contains more than {maxRows} data rows. The maximum allowed is {maxRows}. Please split the file and import in batches.");

            var dict = new Dictionary<string, string>(headers.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
                dict[header] = csv.GetField(header)?.Trim() ?? string.Empty;
            rows.Add(dict);
        }

        return new ParsedFile { Headers = headers, Rows = rows, Format = FileFormat.Csv };
    }
}
