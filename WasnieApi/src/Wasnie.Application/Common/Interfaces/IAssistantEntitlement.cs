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

    /// <summary>
    /// The translation key to refuse this turn with, or null when the tenant may spend tokens (KAN-77, KAN-80, KAN-83).
    ///
    /// ★ ONE QUESTION, NOT TWO. A trial runs out of its one-off allowance; a paying tenant runs out of the month's
    /// included tokens AND its purchased boost. The refusals differ — one leads to the paywall, the other to buying a
    /// boost — but the moment of asking is identical, and splitting it into two calls is how a handler ends up checking
    /// one and forgetting the other. Returning the KEY keeps the distinction where it belongs: in the answer.
    ///
    /// Asked only by the handlers that make the model run, AFTER <see cref="RequireAsync"/> — being out of tokens is not
    /// the same as not being entitled.
    /// </summary>
    Task<string?> TokenRefusalKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Shown to a TRIAL account that spent its allowance: the way forward is to subscribe.</summary>
    public const string TrialAllowanceExhaustedKey = "ASSISTANT.ERROR_TRIAL_LIMIT_REACHED";

    /// <summary>
    /// Shown to a PAYING tenant whose included tokens and boost are both gone: the way forward is to buy a boost.
    /// A different key from the trial's on purpose — the same sentence would send a paying customer to a paywall they
    /// are already past (§C3).
    /// </summary>
    public const string TokensExhaustedKey = "ASSISTANT.ERROR_TOKENS_EXHAUSTED";
}
