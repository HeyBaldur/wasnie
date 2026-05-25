using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Domain.Compensation.Rules;

public sealed class Trigger
{
    public int _schema { get; init; } = 1;
    public LogicalOperator LogicalOperator { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = [];

    public static Trigger Always() => new() { Conditions = [] };

    public static Trigger When(LogicalOperator @operator, IReadOnlyList<Condition> conditions) =>
        new() { LogicalOperator = @operator, Conditions = conditions };
}
