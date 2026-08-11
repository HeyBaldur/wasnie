using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// The current answer to "may this user use the assistant?": tenant admins on a PAID plan.
///
/// ★ TWO INDEPENDENT GATES, and they are not interchangeable. The seat (role, today) is about the
/// PERSON — a colleague without one must not see the entry point at all. The plan is about the
/// TENANT — the admin of a Free workspace may see it locked, because upgrading is a thing they can
/// actually do. That is why <see cref="RequiresPaidPlanAsync"/> exists: it is the difference between
/// "hide this" and "offer this", and only this class can tell them apart.
///
/// ★ THE ROLE CHECK LIVES HERE AND NOWHERE ELSE. It is an implementation detail of the entitlement,
/// not the definition of it — see <see cref="IAssistantEntitlement"/> for why the distinction matters.
///
/// ★ HOW THIS BECOMES PER-SEAT BILLING, so the next person does not have to guess: the seat is a
/// per-USER flag hanging off the tenant's subscription. When that exists, this method becomes
/// "admin OR the user's seat is active", and later just "the user's seat is active" once admins get a
/// seat of their own at signup. Either way it is an edit INSIDE this class — the endpoints, the
/// handlers, the frontend gate and the tests all keep calling the same question and never learn that
/// the answer changed shape. That is the entire design.
/// </summary>
public sealed class AssistantEntitlement(
    IClaimsService claimsService,
    IPaidPlanGate paidPlanGate)
    : IAssistantEntitlement
{
    private const string Feature = "Assistant.Use";
    private const string FeatureLabel = "The AI assistant";

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
        => HasSeat() && await paidPlanGate.IsOnPaidPlanAsync(cancellationToken);

    public async Task<bool> RequiresPaidPlanAsync(CancellationToken cancellationToken = default)
        => HasSeat() && !await paidPlanGate.IsOnPaidPlanAsync(cancellationToken);

    public async Task RequireAsync(CancellationToken cancellationToken = default)
    {
        // Order matters. The plan is checked FIRST so a tenant admin on Free gets the answer that is
        // true and actionable ("not in your plan") instead of a bare Forbidden that reads like a bug.
        if (!HasSeat())
            throw new ForbiddenException(Feature);

        await paidPlanGate.RequirePaidPlanAsync(FeatureLabel, cancellationToken);
    }

    // Today: the tenant admin, and only the tenant admin. Tomorrow: this line reads the seat.
    private bool HasSeat() =>
        string.Equals(claimsService.GetRole(), nameof(Role.TenantAdmin), StringComparison.Ordinal);
}
