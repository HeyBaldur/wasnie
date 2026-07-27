using Wasnie.Domain.Compensation.Enrichment;

namespace Wasnie.Application.Compensation.Enrichment;

/// <summary>
/// In-memory snapshot of a tenant's category lookup table, built ONCE per ingest batch so resolving a
/// category never costs a DB round-trip per transaction (no N+1). Pure and allocation-cheap: two small
/// case-insensitive dictionaries keyed by the trimmed input value.
///
/// Matching mirrors exactly how the engine compares strings (trim + <see cref="StringComparer.OrdinalIgnoreCase"/>),
/// so a mapping resolves the same way a trigger would later compare the resulting category.
/// </summary>
public sealed class CategoryResolver
{
    private readonly IReadOnlyDictionary<string, string> _bySku;
    private readonly IReadOnlyDictionary<string, string> _byName;

    /// <summary>An empty resolver — every lookup returns null. Used when a tenant has no mappings.</summary>
    public static readonly CategoryResolver Empty = new(
        new Dictionary<string, string>(), new Dictionary<string, string>());

    private CategoryResolver(
        IReadOnlyDictionary<string, string> bySku,
        IReadOnlyDictionary<string, string> byName)
    {
        _bySku = bySku;
        _byName = byName;
    }

    /// <summary>
    /// Builds a resolver from the tenant's mappings. The unique index guarantees at most one mapping per
    /// (field, value), but this is defensive against a collation edge (DB "equal" vs Ordinal-CI "not"):
    /// first write wins, deterministically, so it can never throw while ingesting a real sale.
    /// </summary>
    public static CategoryResolver FromMappings(IEnumerable<(string InputField, string InputValue, string Category)> mappings)
    {
        var bySku = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (field, value, category) in mappings)
        {
            var key = value.Trim();
            if (string.Equals(field, CategoryMapping.Fields.ProductSku, StringComparison.OrdinalIgnoreCase))
                bySku.TryAdd(key, category);
            else if (string.Equals(field, CategoryMapping.Fields.ProductName, StringComparison.OrdinalIgnoreCase))
                byName.TryAdd(key, category);
        }

        return bySku.Count == 0 && byName.Count == 0 ? Empty : new CategoryResolver(bySku, byName);
    }

    /// <summary>
    /// Resolves the category for a transaction: ProductSku FIRST, ProductName as the fallback. Returns
    /// null when neither matches — the transaction stays uncategorized (visible, still processable).
    /// The SKU-first order means that the day HubSpot starts sending hs_sku, it takes over automatically
    /// without any mapping change.
    /// </summary>
    public string? Resolve(string? productSku, string? productName)
    {
        if (!string.IsNullOrWhiteSpace(productSku) &&
            _bySku.TryGetValue(productSku.Trim(), out var bySku))
            return bySku;

        if (!string.IsNullOrWhiteSpace(productName) &&
            _byName.TryGetValue(productName.Trim(), out var byName))
            return byName;

        return null;
    }
}
