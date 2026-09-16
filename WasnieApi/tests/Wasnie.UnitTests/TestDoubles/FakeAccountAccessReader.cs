using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Subscription;

namespace Wasnie.UnitTests.TestDoubles;

/// <summary>
/// A tenant's access state, stated outright (KAN-77). Defaults to a PAYING account for every tenant, so tests
/// whose subject is a handler's own logic keep exercising it instead of the paywall. Pass a state when the
/// access IS the subject; <see cref="Set"/> changes one tenant.
/// </summary>
public sealed class FakeAccountAccessReader(AccountAccessState state = AccountAccessState.Active) : IAccountAccessReader
{
    private readonly Dictionary<Guid, AccountAccess> _byTenant = new();

    public void Set(Guid tenantId, AccountAccessState tenantState) => _byTenant[tenantId] = Of(tenantState);

    public Task<AccountAccess?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult<AccountAccess?>(_byTenant.GetValueOrDefault(tenantId) ?? Of(state));

    public Task<IReadOnlyDictionary<Guid, AccountAccess>> GetManyAsync(
        IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, AccountAccess>>(
            tenantIds.Distinct().ToDictionary(id => id, id => _byTenant.GetValueOrDefault(id) ?? Of(state)));

    private static AccountAccess Of(AccountAccessState s) => s switch
    {
        AccountAccessState.Trial => new AccountAccess(s, null, DateTimeOffset.UtcNow.AddDays(3), 3),
        AccountAccessState.Active => new AccountAccess(s, null, null, null),
        _ => new AccountAccess(s, AccountLockReason.TrialEnded, DateTimeOffset.UtcNow.AddDays(-1), 0),
    };
}
