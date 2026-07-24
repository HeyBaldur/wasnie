namespace Wasnie.Application.Models.Imports;

public sealed class TransactionImportColumnMapping
{
    public required string ReferenceNumberColumn { get; init; }
    // Optional: whether a payee is required is tenant-configurable (Transaction.PayeeId), and a
    // blank payee is a supported outcome — the row imports Unassigned, exactly like a HubSpot deal
    // whose owner matches no payee. A tenant with the setting Optional must therefore be able to
    // import a file that has no payee-code column at all, so this cannot be `required`.
    public string? PayeeCodeColumn { get; init; }
    public required string AmountColumn { get; init; }
    public required string CurrencyColumn { get; init; }
    public required string TransactionDateColumn { get; init; }
    public string? ExternalIdColumn { get; init; }
    public string? QuantityColumn { get; init; }
    // Optional human-readable label of the sale. Unmapped → the transaction keeps a null Description.
    public string? DescriptionColumn { get; init; }
    // What was sold. Optional: unmapped → null, exactly like a HubSpot line item with no catalogued
    // product. Kept separate from Description so a rule can eventually filter on the SKU.
    public string? ProductNameColumn { get; init; }
    public string? ProductSkuColumn { get; init; }
}
