using Wasnie.Domain.Common;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Domain.Compensation.ValueObjects;

public sealed class Percentage : ValueObject
{
    public decimal Value { get; }

    private Percentage(decimal value) => Value = value;

    public static Result<Percentage> Create(decimal value)
    {
        if (value < 0m || value > 1m)
        {
            return Result<Percentage>.Failure("Percentage must be between 0 and 1 (inclusive).");
        }

        return Result<Percentage>.Success(new Percentage(value));
    }

    public static Percentage FromPercent(decimal percent) =>
        Create(percent / 100m).Value!;

    public decimal ToPercent() => Value * 100m;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{ToPercent():F2}%";
}
