using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// The current answer to "may this user use the assistant?": tenant admins of an account with access.
///
/// ★ TWO INDEPENDENT GATES, and they are not interchangeable. The seat (role, today) is about the
/// PERSON — a colleague without one must not see the entry point at all. The plan is about the
/// TENANT — the admin of a locked workspace may see it locked, because paying is a thing they can
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
    IPaidPlanGate paidPlanGate,
    IAccountAccessReader accessReader,
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IOptions<BillingOptions> billingOptions)
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
        // Order matters. The plan is checked FIRST so a tenant admin of a locked account gets the answer that
        // is true and actionable ("not in your plan") instead of a bare Forbidden that reads like a bug.
        if (!HasSeat())
            throw new ForbiddenException(Feature);

        await paidPlanGate.RequirePaidPlanAsync(FeatureLabel, cancellationToken);
    }

    /// <summary>
    /// ★ KAN-77 / KAN-80 — THE TRIAL'S SAFETY NET. A trial gets the assistant, but every turn runs a large model at
    /// our expense, so a trial account may consume at most <see cref="BillingOptions.TrialAssistantTokenLimit"/>
    /// tokens (input + output, tenant-wide, every user together). Paying accounts have no limit.
    ///
    /// ★ TOKENS SPENT, NOT QUESTIONS ASKED (KAN-80). It reads the same sum the usage meter shows
    /// (<see cref="Wasnie.Application.Assistant.Common.AssistantTokenMeter"/>), so the screen and the refusal never
    /// disagree. A turn that starts under the limit may end above it — the check is before the turn, and a turn's size is
    /// only known afterwards. That overshoot is bounded by one turn and is the honest trade against cutting an answer in half.
    /// </summary>
    public async Task<bool> IsTrialAllowanceExhaustedAsync(CancellationToken cancellationToken = default)
    {
        if (!tenantContext.IsResolved)
            return false; // RequireAsync already refuses a request with no tenant.

        var access = await accessReader.GetAsync(tenantContext.TenantId, cancellationToken);
        if (access?.State != AccountAccessState.Trial)
            return false;

        var used = await Wasnie.Application.Assistant.Common.AssistantTokenMeter.UsedAsync(
            db, tenantContext.TenantId, since: null, cancellationToken);

        return used >= billingOptions.Value.TrialAssistantTokenLimit;
    }

    // Today: the tenant admin, and only the tenant admin. Tomorrow: this line reads the seat.
    private bool HasSeat() =>
        string.Equals(claimsService.GetRole(), nameof(Role.TenantAdmin), StringComparison.Ordinal);
}
