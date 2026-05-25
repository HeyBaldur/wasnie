namespace Wasnie.Domain.Compensation.Rules;

public sealed class RateTier
{
    public decimal From { get; init; }
    public decimal? To { get; init; }
    public decimal Rate { get; init; }
}
