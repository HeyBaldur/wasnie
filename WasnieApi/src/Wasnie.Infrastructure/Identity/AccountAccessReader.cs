using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Subscription;

namespace Wasnie.Infrastructure.Identity;

/// <inheritdoc cref="IAccountAccessReader"/>
/// <remarks>
/// Tenant query filters are ignored on purpose: the anonymous HubSpot callback and the background sync ask
/// about a tenant that is not the ambient one, and the paywall middleware must see the tenant even before
/// anything else in the request has run.
/// </remarks>
public sealed class AccountAccessReader(IApplicationDbContext db, IClock clock) : IAccountAccessReader
{
    public async Task<AccountAccess?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var all = await GetManyAsync([tenantId], cancellationToken);
        return all.GetValueOrDefault(tenantId);
    }

    public async Task<IReadOnlyDictionary<Guid, AccountAccess>> GetManyAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken = default)
    {
        if (tenantIds.Count == 0)
            return new Dictionary<Guid, AccountAccess>();

        var trials = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.TrialEndsAt })
            .ToListAsync(cancellationToken);

        var subscriptions = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId))
            .Select(s => new { s.TenantId, s.Status, HasStripe = s.StripeSubscriptionId != null })
            .ToDictionaryAsync(s => s.TenantId, cancellationToken);

        var now = clock.UtcNowOffset;
        return trials.ToDictionary(
            t => t.Id,
            t =>
            {
                var sub = subscriptions.GetValueOrDefault(t.Id);
                return AccountAccessPolicy.Resolve(t.TrialEndsAt, sub?.Status, sub?.HasStripe ?? false, now);
            });
    }
}
