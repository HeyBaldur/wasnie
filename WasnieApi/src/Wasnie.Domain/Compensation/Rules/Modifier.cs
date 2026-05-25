using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Domain.Compensation.Rules;

public sealed class Modifier
{
    public int _schema { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public ModifierType Type { get; init; }
    public decimal Factor { get; init; }
    public Trigger? Trigger { get; init; }
}
