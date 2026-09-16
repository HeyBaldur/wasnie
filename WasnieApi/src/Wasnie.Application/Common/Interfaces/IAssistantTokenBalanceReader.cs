using Wasnie.Domain.Assistant;
using Wasnie.Domain.Subscription;

namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// The tenant's assistant token balance (KAN-83) — included allowance plus purchased boost, derived on every read.
///
/// ★ AN INTERFACE SO THE GATE AND THE SCREENS SHARE ONE ANSWER. The entitlement that refuses a turn lives in
/// Infrastructure and the screens read a query in Application; without this they would each assemble the balance
/// themselves and drift apart.
/// </summary>
public interface IAssistantTokenBalanceReader
{
    /// <summary>Null when the tenant does not exist, or is locked — a locked account has no balance to act on.</summary>
    /// <param name="known">
    /// The tenant's access state when the caller has already resolved it. Passed to avoid resolving it twice in one
    /// request — and, more than efficiency, to guarantee the balance is computed against the SAME state the caller
    /// acted on, rather than one re-read a moment later.
    /// </param>
    Task<AssistantTokenBalance?> GetAsync(
        Guid tenantId, CancellationToken cancellationToken = default, AccountAccess? known = null);
}
