using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// Reads the tenant's tier from the DATABASE, not from the JWT.
///
/// ★ That is the whole point and it must not be "optimised" into a claim. The tier changes the moment
/// Stripe confirms a payment; a claim only changes when the token is reissued. A claim-based gate would
/// keep refusing the assistant to someone who has just paid for it (until they sign out and back in),
/// and — the direction that actually costs money — would keep GRANTING the CRM sync to a tenant who
/// downgraded, for as long as their token lives. The read is a single indexed lookup by primary key.
///
/// Mirrors <see cref="TierLimitChecker"/>, which reads the tier the same way for the same reason.
/// </summary>
public sealed class PaidPlanGate(
    IApplicationDbContext db,
    ITenantContext tenantContext)
    : IPaidPlanGate
{
    public async Task<bool> IsOnPaidPlanAsync(CancellationToken cancellationToken = default)
    {
        // No tenant resolved = no plan proven. Answering "true" here would hand the metered features
        // to any request that arrives without a tenant, so the unknown case is a refusal.
        if (!tenantContext.IsResolved)
            return false;

        var tier = await GetTierAsync(tenantContext.TenantId, cancellationToken);
        return tier is not null && TierFeatures.IncludesPaidFeatures(tier.Value);
    }

    public async Task RequirePaidPlanAsync(string feature, CancellationToken cancellationToken = default)
    {
        if (await IsOnPaidPlanAsync(cancellationToken))
            return;

        var tier = tenantContext.IsResolved
            ? await GetTierAsync(tenantContext.TenantId, cancellationToken)
            : null;

        throw new PaidPlanRequiredException(
            feature,
            (tier ?? Tier.Free).ToString(),
            TierFeatures.MinimumPaidTier.ToString());
    }

    private async Task<Tier?> GetTierAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (Tier?)t.Tier)
            .FirstOrDefaultAsync(cancellationToken);
}
