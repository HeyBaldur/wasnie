using Wasnie.Domain.Subscription;

namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Reads a tenant's facts (trial end, subscription) from the DATABASE and applies
/// <see cref="AccountAccessPolicy"/>. Never from a JWT claim: a payment must unlock the account on the next
/// request, and a cancellation must lock it, not whenever a token happens to be reissued.
/// </summary>
public interface IAccountAccessReader
{
    /// <summary>Null when the tenant does not exist.</summary>
    Task<AccountAccess?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The same rule for many tenants in two queries — for fan-out jobs. Missing tenants are absent.</summary>
    Task<IReadOnlyDictionary<Guid, AccountAccess>> GetManyAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken = default);
}
