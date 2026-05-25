using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Domain.Compensation.Rules;

public sealed class Cap
{
    public int _schema { get; init; } = 1;
    public Money Amount { get; init; } = Money.Zero("USD");
    public CapScope Scope { get; init; }
}
