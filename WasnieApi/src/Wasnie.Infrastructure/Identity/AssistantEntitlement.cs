using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
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
    IAssistantTokenBalanceReader balanceReader,
    ITenantContext tenantContext)
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
    /// ★ KAN-77 / KAN-80 / KAN-83 — THE CEILING, for every kind of account. Every turn runs a large model at our
    /// expense, and the price is flat with unlimited users, so a tenant spends against a balance: a trial its one-off
    /// allowance, a paying tenant the month's included tokens plus whatever boost it bought. Tenant-wide in both cases —
    /// all the users of an account draw on one pool.
    ///
    /// ★ TOKENS SPENT, NOT QUESTIONS ASKED (KAN-80). A turn that starts inside the balance may end outside it — the
    /// check is before the turn, and a turn's size is only known afterwards. That overshoot is bounded by one turn and is
    /// the honest trade against cutting an answer in half.
    /// </summary>
    public async Task<string?> TokenRefusalKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!tenantContext.IsResolved)
            return null; // RequireAsync already refuses a request with no tenant.

        var access = await accessReader.GetAsync(tenantContext.TenantId, cancellationToken);
        if (access is null)
            return null;

        // ★ THE BALANCE IS READ, NOT RECOMPUTED. The same reader feeds the meter on the settings screen, so a tenant
        // never sees "1.2M of 3M used" beside an assistant that refuses to answer.
        var balance = await balanceReader.GetAsync(tenantContext.TenantId, cancellationToken, known: access);
        if (balance is null || !balance.Exhausted)
            return null;

        // ★ WHICH REFUSAL depends on what the tenant can DO about it. A trial subscribes; a paying tenant buys a boost.
        return access.State == AccountAccessState.Trial
            ? IAssistantEntitlement.TrialAllowanceExhaustedKey
            : IAssistantEntitlement.TokensExhaustedKey;
    }

    // Today: the tenant admin, and only the tenant admin. Tomorrow: this line reads the seat.
    private bool HasSeat() =>
        string.Equals(claimsService.GetRole(), nameof(Role.TenantAdmin), StringComparison.Ordinal);
}
