using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.ValueObjects;

/// <summary>
/// Attainment as a ratio — can exceed 1.0 (overachievement).
/// Distinct from <see cref="Percentage"/> (which is capped 0-1 for rule rates).
/// </summary>
public sealed class AttainmentPercentage : ValueObject
{
    /// <summary>Raw ratio, e.g. 0.76 = 76%, 1.20 = 120%.</summary>
    public decimal Value { get; }

    public static readonly AttainmentPercentage Zero = new(0m);

    private AttainmentPercentage(decimal value) => Value = value;

    /// <summary>
    /// Computes attainment from achieved and target values.
    /// Returns Zero when target is 0 to avoid division-by-zero (no defined quota).
    /// </summary>
    public static AttainmentPercentage FromAchievedAndTarget(decimal achieved, decimal target)
    {
        if (target <= 0m) return Zero;
        var ratio = Math.Round(achieved / target, 4, MidpointRounding.ToEven);
        return new AttainmentPercentage(ratio < 0m ? 0m : ratio);
    }

    /// <summary>Returns a string like "76%" or "120%".</summary>
    public string ToPercentString() => $"{Math.Round(Value * 100m, 0, MidpointRounding.ToEven):0}%";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => ToPercentString();
}
