namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// ★ THE ONE PLACE that answers "may this user use the assistant?".
///
/// It is an ENTITLEMENT, not a permission, and that distinction is the whole reason this interface
/// exists instead of a <c>Permission.AssistantUse</c> constant. Permissions in Wasnie are derived from
/// the ROLE (see RolePermissions): every TenantAdmin has the same ones, always, and there is no way to
/// grant one to a single person. The assistant is going the other way — the plan is per-seat billing,
/// where the admin pays a monthly fee for each additional user they want to switch on. That is a
/// per-USER fact tied to the subscription, which the role map cannot express.
///
/// TODAY it answers "yes" only for the tenant admin. The point is not what it answers, it is that it
/// answers in ONE place: turning this into a paid per-seat flag must be an edit to the implementation,
/// not a hunt for scattered <c>if (role == TenantAdmin)</c> checks across endpoints. Nothing outside
/// the implementation may ask about the role to decide assistant access.
/// </summary>
public interface IAssistantEntitlement
{
    /// <summary>True when the current principal may use the assistant. Never throws.</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the ONLY thing between this user and the assistant is the tenant's plan — they hold
    /// the seat, the workspace is on Free. This is the one case where the client should render a
    /// LOCKED entry point with an upgrade path rather than hiding it: the user is not overstepping
    /// anything, they are looking at something they can buy. Never throws.
    /// </summary>
    Task<bool> RequiresPaidPlanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="Common.Exceptions.ForbiddenException"/> when the current principal is not
    /// entitled. Every assistant command and query calls this first.
    /// </summary>
    Task RequireAsync(CancellationToken cancellationToken = default);
}
