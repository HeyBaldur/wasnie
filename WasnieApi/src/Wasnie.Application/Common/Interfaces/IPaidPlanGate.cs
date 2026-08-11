namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// ★ THE ONE PLACE that answers "is this tenant on a plan that includes the metered features?".
///
/// It is a BILLING state, not a permission and not a quota:
/// <list type="bullet">
///   <item>A permission (see IAuthorizationService) says what a ROLE may do. It never changes because
///         someone paid, and a denial there means the user overstepped their authority.</item>
///   <item>A tier LIMIT (see ITierLimitChecker) says how many rows a tenant may create. It is a
///         counter, and the tenant is doing nothing wrong until they hit it.</item>
///   <item>This says the capability is not part of what they bought. Nobody overstepped anything —
///         the answer changes the day they upgrade, and it changes for the whole tenant at once.</item>
/// </list>
///
/// That difference is why denials here are NOT audited as security events: filling the audit trail
/// with "a free tenant clicked the assistant" would bury the entries that mean someone tried to
/// exceed their authority. It is also why the UI is allowed to ask and show a locked control with an
/// upgrade path, where a permission denial is hidden outright.
/// </summary>
public interface IPaidPlanGate
{
    /// <summary>
    /// True when the current tenant's plan includes the metered features. Never throws — an
    /// unresolved or missing tenant answers <c>false</c>, because "we don't know" must not spend money.
    /// </summary>
    Task<bool> IsOnPaidPlanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="Common.Exceptions.PaidPlanRequiredException"/> when the tenant is on Free.
    /// Every command and query behind a metered capability calls this first.
    /// </summary>
    /// <param name="feature">The capability being refused, for the error payload (e.g. "HubSpot").</param>
    Task RequirePaidPlanAsync(string feature, CancellationToken cancellationToken = default);
}
