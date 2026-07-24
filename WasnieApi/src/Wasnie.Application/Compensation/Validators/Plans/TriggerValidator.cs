using FluentValidation;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Compensation.Validators.Plans;

/// <summary>
/// Rejects rule triggers the engine could never honour.
///
/// Nothing validated this before: a condition naming a field the engine does not resolve saved
/// happily and then silently never fired. Every rule with a condition in the tenant today is in that
/// state — two filter on a field that does not exist, two on a value the field can never hold. Once
/// commissions are routed by these filters, a rule that quietly never fires is a rep who quietly
/// never gets paid, so this is a hard rejection rather than a warning.
/// </summary>
public sealed class TriggerValidator : AbstractValidator<Trigger>
{
    public TriggerValidator()
    {
        RuleForEach(t => t.Conditions).ChildRules(condition =>
        {
            condition.RuleFor(c => c.Field)
                .Must(field => TriggerFieldCatalog.Find(field) is not null)
                .WithMessage(c =>
                    $"'{c.Field}' is not a transaction attribute this engine can read, so the rule " +
                    $"would never match. Valid fields: {string.Join(", ", TriggerFieldCatalog.Fields.Select(f => f.Field))}.");

            // The operator has to be one the evaluator for that field's type implements — offering
            // ordering on a string, or a set on a number, would be a filter that never fires.
            condition.RuleFor(c => c.Operator)
                .Must((c, op) =>
                {
                    var definition = TriggerFieldCatalog.Find(c.Field);
                    return definition is null || definition.Operators.Contains(op);
                })
                .WithMessage(c =>
                {
                    var definition = TriggerFieldCatalog.Find(c.Field);
                    var allowed = definition is null
                        ? string.Empty
                        : string.Join(", ", definition.Operators);
                    return $"Operator '{c.Operator}' cannot be applied to '{c.Field}'. Allowed: {allowed}.";
                });

            // In/NotIn read Value.Set; Raw is ignored. An empty set matches nothing (In) or
            // everything (NotIn) — both are almost certainly not what the author meant.
            condition.RuleFor(c => c.Value)
                .Must((c, value) =>
                    !TriggerFieldCatalog.UsesSet(c.Operator) ||
                    (value?.Set is { Count: > 0 } && value.Set.All(s => !string.IsNullOrWhiteSpace(s))))
                .WithMessage(c =>
                    $"Operator '{c.Operator}' on '{c.Field}' needs a non-empty list of values.");

            // The other operators read Value.Raw.
            condition.RuleFor(c => c.Value)
                .Must((c, value) =>
                    TriggerFieldCatalog.UsesSet(c.Operator) || !string.IsNullOrWhiteSpace(value?.Raw))
                .WithMessage(c => $"Condition on '{c.Field}' needs a value.");
        });
    }
}
