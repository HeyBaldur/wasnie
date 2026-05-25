using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Domain.Compensation.Rules;

public sealed class Condition
{
    public string Field { get; init; } = string.Empty;
    public ConditionOperator Operator { get; init; }
    public ConditionValue Value { get; init; } = new();
}
