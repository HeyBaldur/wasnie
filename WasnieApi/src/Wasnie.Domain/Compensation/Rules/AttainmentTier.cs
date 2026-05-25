namespace Wasnie.Domain.Compensation.Rules;

public sealed class AttainmentTier
{
    public decimal AttainmentFrom { get; init; }
    public decimal? AttainmentTo { get; init; }
    public decimal Rate { get; init; }
}
