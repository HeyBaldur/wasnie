namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// One selectable trigger field for the rule builder.
///
/// <paramref name="Operators"/> lists only what the engine's evaluator for this field's type actually
/// implements, so the UI can offer exactly those. Showing an operator the engine ignores would produce
/// a rule that looks configured and never fires.
///
/// <paramref name="UsesSet"/> flags per operator whether the value is a LIST (In/NotIn read
/// <c>Value.Set</c>) rather than a single value, so the form knows which input to render.
/// </summary>
public sealed record TriggerFieldDto(
    string Field,
    string ValueType,
    IReadOnlyList<TriggerOperatorDto> Operators);

public sealed record TriggerOperatorDto(
    string Operator,
    bool UsesSet);
