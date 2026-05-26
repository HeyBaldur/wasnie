namespace Wasnie.Application.Models.Imports;

public enum FileFormat { Csv, Xlsx }

public sealed class ParsedFile
{
    public required string[] Headers { get; init; }
    public required List<Dictionary<string, string>> Rows { get; init; }
    public required FileFormat Format { get; init; }
}
