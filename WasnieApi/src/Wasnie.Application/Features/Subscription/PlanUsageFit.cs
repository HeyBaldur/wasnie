using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;

namespace Wasnie.Application.Features.Subscription;

/// <summary>Why a tenant's current usage does not fit a plan, or null when it fits.</summary>
public sealed record PlanUsageExcess(string Reason, int Current, int Limit);

/// <summary>
/// ★ ONE RULE for "does what this tenant already has fit into that plan?", shared by checkout and plan change —
/// the two used to carry their own copy. Null limits are unlimited and never exceed.
/// </summary>
public static class PlanUsageFit
{
    public static async Task<PlanUsageExcess?> CheckAsync(
        IApplicationDbContext db, SubscriptionPlanDefinition plan, CancellationToken cancellationToken)
    {
        if (plan.MaxPayees is int maxPayees)
        {
            var payees = await db.Payees.CountAsync(cancellationToken);
            if (payees > maxPayees)
                return new PlanUsageExcess("payees", payees, maxPayees);
        }

        if (plan.MaxPlans is int maxPlans)
        {
            var plans = await db.CompensationPlans.CountAsync(cancellationToken);
            if (plans > maxPlans)
                return new PlanUsageExcess("plans", plans, maxPlans);
        }

        return null;
    }
}
