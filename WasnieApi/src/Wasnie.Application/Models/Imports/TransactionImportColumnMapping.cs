namespace Wasnie.Application.Models.Imports;

public sealed class TransactionImportColumnMapping
{
    public required string ReferenceNumberColumn { get; init; }
    public required string PayeeCodeColumn { get; init; }
    public required string AmountColumn { get; init; }
    public required string CurrencyColumn { get; init; }
    public required string TransactionDateColumn { get; init; }
    public string? ExternalIdColumn { get; init; }
}
