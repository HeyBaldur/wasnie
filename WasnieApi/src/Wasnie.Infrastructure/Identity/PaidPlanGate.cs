using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// Decides whether the tenant may use the metered capabilities (AI assistant, HubSpot).
///
/// ★★ KAN-77: THE QUESTION IS NOW "DOES THE ACCOUNT HAVE ACCESS", NOT "WHICH TIER". The free plan is gone.
/// A trial gets the full product — assistant and HubSpot included, by product decision — and a paying tenant
/// gets it too; only a Locked account does not. So the answer comes from <see cref="IAccountAccessReader"/>,
/// the same rule the paywall uses. (The assistant's trial usage limit is a separate allowance, not this gate.)
///
/// ★ STILL READ FROM THE DATABASE, never from the JWT: a cancellation must stop the CRM sync on the next
/// request, not when a token expires. And "we don't know" (no tenant) is still a refusal.
/// </summary>
public sealed class PaidPlanGate(
    ITenantContext tenantContext,
    IAccountAccessReader accessReader,
    ISubscriptionPlanCatalog catalog)
    : IPaidPlanGate
{
    public async Task<bool> IsOnPaidPlanAsync(CancellationToken cancellationToken = default)
    {
        // No tenant resolved = nothing proven. Answering "true" here would hand the metered features to any
        // request that arrives without a tenant, so the unknown case is a refusal.
        if (!tenantContext.IsResolved)
            return false;

        var access = await accessReader.GetAsync(tenantContext.TenantId, cancellationToken);
        return access?.HasAccess == true;
    }

    public async Task RequirePaidPlanAsync(string feature, CancellationToken cancellationToken = default)
    {
        if (await IsOnPaidPlanAsync(cancellationToken))
            return;

        // The refusal names the account's state (Locked) where it used to name a tier, and offers the plan a
        // locked account can buy. The 403 shape the client reads is unchanged.
        var access = tenantContext.IsResolved
            ? await accessReader.GetAsync(tenantContext.TenantId, cancellationToken)
            : null;

        throw new PaidPlanRequiredException(
            feature,
            access?.State.ToString() ?? "Unknown",
            catalog.Default.Code);
    }
}
