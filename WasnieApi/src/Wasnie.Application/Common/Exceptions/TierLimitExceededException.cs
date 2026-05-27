namespace Wasnie.Application.Common.Exceptions;

public sealed class TierLimitExceededException(
    string tier,
    string resourceType,
    int currentCount,
    int limit,
    string? upgradeTier = null)
    : Exception($"Your {tier} plan is limited to {limit} {resourceType}.")
{
    public string Tier { get; } = tier;
    public string ResourceType { get; } = resourceType;
    public int CurrentCount { get; } = currentCount;
    public int Limit { get; } = limit;
    public string? UpgradeTier { get; } = upgradeTier;
}
