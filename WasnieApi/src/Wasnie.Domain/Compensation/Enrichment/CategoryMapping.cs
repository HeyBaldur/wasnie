using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Enrichment;

/// <summary>
/// One admin-maintained enrichment rule: "when a transaction's <see cref="InputField"/> equals
/// <see cref="InputValue"/>, its Category is <see cref="Category"/>."
///
/// This is the lookup table the ICM owns so the CRM does not have to send clean data — the reason it
/// exists is that a real tenant's discriminating value (e.g. LAP-12) arrived in ProductName while the
/// rule filtered on ProductSku, and the rule silently never fired. The mapping decouples "what the rule
/// filters on" (a stable, discrete Category) from "which raw field the origin happened to populate".
///
/// Per-tenant config table, mirrors <see cref="Wasnie.Domain.Settings.FieldRequirementSetting"/>. A
/// UNIQUE index on (TenantId, InputField, InputValue) makes a duplicate mapping a HARD error instead of
/// silent precedence — two rows claiming the same input must never both win.
/// </summary>
public sealed class CategoryMapping : Entity
{
    /// <summary>The transaction attributes an enrichment rule may read. Discrete on purpose.</summary>
    public static class Fields
    {
        public const string ProductSku = "ProductSku";
        public const string ProductName = "ProductName";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ProductSku, ProductName };

        public static bool IsValid(string? field) =>
            field is not null && All.Contains(field.Trim());
    }

    public const int MaxInputValueLength = 500;
    public const int MaxCategoryLength = 200;

    public Guid TenantId { get; private set; }
    public string InputField { get; private set; } = string.Empty;
    public string InputValue { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;

    private CategoryMapping() { }

    public static CategoryMapping Create(
        Guid id,
        Guid tenantId,
        string inputField,
        string inputValue,
        string category)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");

        var normalizedField = NormalizeField(inputField);
        var normalizedValue = NormalizeValue(inputValue);
        var normalizedCategory = NormalizeCategory(category);

        return new CategoryMapping
        {
            Id = id,
            TenantId = tenantId,
            InputField = normalizedField,
            InputValue = normalizedValue,
            Category = normalizedCategory,
        };
    }

    public void Update(string inputField, string inputValue, string category)
    {
        InputField = NormalizeField(inputField);
        InputValue = NormalizeValue(inputValue);
        Category = NormalizeCategory(category);
    }

    private static string NormalizeField(string inputField)
    {
        if (!Fields.IsValid(inputField))
            throw new DomainException(
                $"Input field '{inputField}' is not supported. Expected one of: {string.Join(", ", Fields.All)}.");
        return inputField.Trim();
    }

    private static string NormalizeValue(string inputValue)
    {
        if (string.IsNullOrWhiteSpace(inputValue))
            throw new DomainException("Input value is required.");
        var trimmed = inputValue.Trim();
        if (trimmed.Length > MaxInputValueLength)
            throw new DomainException($"Input value must not exceed {MaxInputValueLength} characters.");
        return trimmed;
    }

    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new DomainException("Category is required.");
        var trimmed = category.Trim();
        if (trimmed.Length > MaxCategoryLength)
            throw new DomainException($"Category must not exceed {MaxCategoryLength} characters.");
        return trimmed;
    }
}
