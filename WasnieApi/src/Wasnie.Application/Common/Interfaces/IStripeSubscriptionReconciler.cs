namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Brings the current tenant's stored subscription in line with Stripe, applying the SAME rules as the webhook. The
/// safety net for a webhook that never arrived: the stored row decides access, so a missed cancellation would
/// otherwise leave a non-paying account open indefinitely. Reads Stripe; writes only our database.
/// </summary>
public interface IStripeSubscriptionReconciler
{
    /// <returns>True when the stored subscription changed.</returns>
    Task<bool> ReconcileAsync(CancellationToken cancellationToken = default);
}
