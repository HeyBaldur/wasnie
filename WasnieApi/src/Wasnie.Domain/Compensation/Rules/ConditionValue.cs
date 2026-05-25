using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Domain.Compensation.Rules;

public sealed class ConditionValue
{
    public ConditionValueType Type { get; init; }
    public string Raw { get; init; } = string.Empty;
    public IReadOnlyList<string>? Set { get; init; }
}
