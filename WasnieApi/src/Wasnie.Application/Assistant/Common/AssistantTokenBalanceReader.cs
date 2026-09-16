using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Subscription;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// The tenant's assistant token balance, assembled from the four facts it is derived from (KAN-83):
/// the allowance its state grants, what it spent this period, the boosts it bought, and what past periods already
/// charged to those boosts.
///
/// ★ ONE READER, EVERY CALLER. The refusal that stops a turn, the meter in Settings, the billing screen and the
/// checkout all ask this — the same reason <see cref="AssistantTokenMeter"/> is shared. A screen and a refusal that
/// compute the balance separately will disagree the first time either changes.
///
/// ★ THE PERIOD IS STRIPE'S, NOT THE CALENDAR'S. A paying tenant's allowance resets when its subscription renews, not
/// on the first of the month, so the window comes from <c>CurrentPeriodStart</c>. When Stripe has not told us the start
/// yet, the subscription's own creation is the honest fallback — it is when this paid relationship began.
///
/// ★ TRIAL AND PAID ARE DIFFERENT ALLOWANCES OVER DIFFERENT WINDOWS. A trial gets its one-off allowance counted since
/// the account began and never renewed; a paying tenant gets the plan's monthly allowance counted from the period
/// start. What a trial spent does NOT carry into the first paid period — the window moves with the subscription, so the
/// old consumption falls outside it on its own, with nothing to reset.
/// </summary>
public sealed class AssistantTokenBalanceReader(
    IApplicationDbContext db,
    IAccountAccessReader accessReader,
    IOptions<BillingOptions> billingOptions,
    IClock clock)
    : IAssistantTokenBalanceReader
{
    public async Task<AssistantTokenBalance?> GetAsync(
        Guid tenantId, CancellationToken cancellationToken = default, AccountAccess? known = null)
    {
        var access = known ?? await accessReader.GetAsync(tenantId, cancellationToken);
        if (access is null)
            return null;

        var options = billingOptions.Value;

        long includedLimit;
        DateTimeOffset? since;

        switch (access.State)
        {
            case AccountAccessState.Trial:
                includedLimit = options.TrialAssistantTokenLimit;
                since = null; // The trial's allowance covers the whole account, not a period.
                break;

            case AccountAccessState.Active:
                includedLimit = options.IncludedAssistantTokensPerMonth;
                since = await CurrentPeriodStartAsync(tenantId, cancellationToken);
                break;

            default:
                // Locked: there is no allowance to spend and no balance to act on.
                return null;
        }

        var used = await AssistantTokenMeter.UsedAsync(db, tenantId, since, cancellationToken);

        var lots = await db.AssistantTokenBoosts
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId)
            .Select(b => new { b.Tokens, b.PurchasedAt, b.ExpiresAt })
            .ToListAsync(cancellationToken);

        // Every CLOSED period's overage. The current period's is derived from `used` inside the walk, so counting a
        // period here as well as there would charge it twice — closes are written only once a period has ended.
        var charged = await db.AssistantBoostDebits
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId)
            .SumAsync(d => (long?)d.Tokens, cancellationToken) ?? 0;

        return AssistantBoostLedger.Compute(
            includedLimit,
            used,
            lots.Select(l => new BoostLot(l.Tokens, l.PurchasedAt, l.ExpiresAt)),
            charged,
            clock.UtcNowOffset);
    }

    private async Task<DateTimeOffset?> CurrentPeriodStartAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var subscription = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.StripeSubscriptionId != null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.CurrentPeriodStart, s.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return subscription?.CurrentPeriodStart ?? subscription?.CreatedAt;
    }
}
