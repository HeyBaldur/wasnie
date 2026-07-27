using System.Globalization;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// One transaction attribute a rule trigger can filter on: how the engine reads it, what type it is,
/// and therefore which operators actually work.
/// </summary>
/// <param name="Field">Canonical name, matched case-insensitively against <c>Condition.Field</c>.</param>
/// <param name="Resolve">How the engine reads the value off a transaction. Null = the transaction has no value.</param>
public sealed record TriggerFieldDefinition(
    string Field,
    ConditionValueType ValueType,
    Func<CompensationTransaction, string?> Resolve)
{
    /// <summary>Only the operators the evaluator for <see cref="ValueType"/> genuinely honours.</summary>
    public IReadOnlyList<ConditionOperator> Operators => TriggerFieldCatalog.OperatorsFor(ValueType);
}

/// <summary>
/// THE list of attributes a rule can filter on — single source of truth for the engine, the save-time
/// validator and the rule builder's field picker alike.
///
/// It exists because all three used to disagree. The engine resolved three names in a private switch;
/// the UI let an admin type ANY name into a free-text box; nothing validated on save. A typo produced
/// a rule that silently never fired — and once money is routed by these filters, a rule that quietly
/// never fires is a rep who quietly never gets paid. Every rule with a condition in the tenant today
/// is in exactly that state.
///
/// <see cref="TriggerFieldDefinition.Resolve"/> lives here rather than in the calculator so the list
/// and the reading cannot drift: a field that is offered is, by construction, a field the engine reads.
/// </summary>
public static class TriggerFieldCatalog
{
    public static IReadOnlyList<TriggerFieldDefinition> Fields { get; } =
    [
        new("transactionamount", ConditionValueType.Number,
            t => t.Amount.Amount.ToString(CultureInfo.InvariantCulture)),

        new("transactiondate", ConditionValueType.Date,
            t => t.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),

        new("quantity", ConditionValueType.Number,
            t => t.Quantity.ToString(CultureInfo.InvariantCulture)),

        // Origin of the row (Manual / EtlImport / CrmSync) — NOT a business segment. Worth stating
        // because a rule in the tenant filters Source Equal "Enterprise", which can never match.
        new("source", ConditionValueType.String,
            t => t.Source.ToString()),

        new("currency", ConditionValueType.String,
            t => t.Amount.Currency),

        // What was sold. ProductSku is the discrete one — the reason it is its own column.
        new("productsku", ConditionValueType.String,
            t => t.ProductSku),

        new("productname", ConditionValueType.String,
            t => t.ProductName),

        // Enrichment output (WI-ENRICHMENT). The discrete, stable value a rule SHOULD filter on: it is
        // resolved at ingest from the tenant lookup table, so it does not depend on which raw column the
        // origin happened to populate. String field → Equal/NotEqual/In/NotIn, same as the others.
        new("category", ConditionValueType.String,
            t => t.Category),
    ];

    /// <summary>Case-insensitive lookup; null when the name is not a real field.</summary>
    public static TriggerFieldDefinition? Find(string? field) =>
        string.IsNullOrWhiteSpace(field)
            ? null
            : Fields.FirstOrDefault(f =>
                string.Equals(f.Field, field.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The operators each evaluator in CommissionCalculator actually implements. Anything outside this
    /// returns false there, so offering it in the UI would be promising a filter that never fires.
    /// </summary>
    public static IReadOnlyList<ConditionOperator> OperatorsFor(ConditionValueType type) => type switch
    {
        ConditionValueType.String =>
        [
            ConditionOperator.Equal, ConditionOperator.NotEqual,
            ConditionOperator.In, ConditionOperator.NotIn,
        ],
        // Numeric and date evaluators do ordering but have no set semantics.
        ConditionValueType.Number or ConditionValueType.Date =>
        [
            ConditionOperator.Equal, ConditionOperator.NotEqual,
            ConditionOperator.GreaterThan, ConditionOperator.GreaterThanOrEqual,
            ConditionOperator.LessThan, ConditionOperator.LessThanOrEqual,
        ],
        ConditionValueType.Boolean =>
        [
            ConditionOperator.Equal, ConditionOperator.NotEqual,
        ],
        _ => [],
    };

    /// <summary>True when the evaluator reads <c>Value.Set</c> instead of <c>Value.Raw</c>.</summary>
    public static bool UsesSet(ConditionOperator op) =>
        op is ConditionOperator.In or ConditionOperator.NotIn;
}
