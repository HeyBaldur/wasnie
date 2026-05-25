using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.ValueObjects;

public sealed class ModifierApplication : ValueObject
{
    public Guid ModifierId { get; }
    public string ModifierName { get; }
    public decimal FactorApplied { get; }
    public Money AmountBefore { get; }
    public Money AmountAfter { get; }
    public DateTimeOffset AppliedAt { get; }

    private ModifierApplication(
        Guid modifierId,
        string modifierName,
        decimal factorApplied,
        Money amountBefore,
        Money amountAfter,
        DateTimeOffset appliedAt)
    {
        ModifierId = modifierId;
        ModifierName = modifierName;
        FactorApplied = factorApplied;
        AmountBefore = amountBefore;
        AmountAfter = amountAfter;
        AppliedAt = appliedAt;
    }

    public static ModifierApplication Record(
        Guid modifierId,
        string modifierName,
        decimal factorApplied,
        Money amountBefore,
        Money amountAfter) =>
        new(modifierId, modifierName, factorApplied, amountBefore, amountAfter, DateTimeOffset.UtcNow);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return ModifierId;
        yield return AppliedAt;
    }
}
