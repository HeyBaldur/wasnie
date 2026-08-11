namespace Wasnie.Application.Common.Exceptions;

/// <summary>
/// The tenant's plan does not include the capability they asked for. Distinct from
/// <see cref="ForbiddenException"/> on purpose: that one means "you are not allowed", this one means
/// "this is not part of what you bought" — so the client can offer an upgrade instead of hiding the
/// control. Both answer 403; only the payload's <c>error</c> discriminator differs.
/// </summary>
public sealed class PaidPlanRequiredException(string feature, string currentTier, string upgradeTier)
    : Exception($"{feature} is not included in the {currentTier} plan.")
{
    public string Feature { get; } = feature;
    public string CurrentTier { get; } = currentTier;
    public string UpgradeTier { get; } = upgradeTier;
}
