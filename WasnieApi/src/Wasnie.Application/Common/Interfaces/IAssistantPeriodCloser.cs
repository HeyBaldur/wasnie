namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Records a billing period's assistant overage when the period rolls over (KAN-83).
///
/// ★ ASKED WHEREVER THE PERIOD MOVES, which today is the Stripe webhook and the on-read sync. Both apply their change
/// through one applier, so this is asked there once rather than at each call site — an invariant placed on only one of
/// the two paths is one a customer reaches through the other (§D2).
/// </summary>
public interface IAssistantPeriodCloser
{
    /// <summary>
    /// Adds the closing row for the period that just ended, if one ended. Adds nothing when the period is unchanged,
    /// already closed, or there was no previous period. Does NOT save — the caller commits it with its own change.
    /// </summary>
    Task CloseIfRolledOverAsync(
        Guid tenantId,
        DateTimeOffset? previousPeriodStart,
        DateTimeOffset? previousPeriodEnd,
        DateTimeOffset newPeriodStart,
        CancellationToken cancellationToken = default);
}
