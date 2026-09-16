using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Subscription;

namespace Wasnie.Application.Features.Subscription.Queries;

/// <summary>
/// The account's access state for the client: the trial banner ("N days left"), the paywall screen and the
/// assistant's remaining trial messages all read this one response (KAN-77). Exempt from the paywall itself,
/// so a locked user can still learn WHY they are locked.
/// </summary>
public sealed record GetAccountAccessQuery : IRequest<Result<AccountAccessDto>>;

/// <param name="State">Trial | Active | Locked.</param>
/// <param name="LockReason">TrialEnded | SubscriptionEnded | NoSubscription — null unless Locked.</param>
/// <param name="TrialDaysRemaining">Whole days left, rounded up — null unless in trial.</param>
/// <param name="TrialLengthDays">The configured trial length — what "N days left" is out of. Null unless in trial.</param>
/// <param name="AssistantTokensUsed">
/// Assistant tokens consumed (input + output, KAN-80). Trial: since the account began. Active: in the CURRENT billing
/// period only. Null when locked.
/// </param>
/// <param name="AssistantTokenLimit">
/// The allowance <paramref name="AssistantTokensUsed"/> counts against: the trial's one-off allowance, or a paying
/// tenant's included tokens for this period (KAN-83). Null only when locked.
/// </param>
/// <param name="AssistantBoostRemaining">
/// Purchased tokens still available after this period's overage and any expiry (KAN-83). Zero when none were bought.
/// </param>
/// <param name="AssistantBoostExpiresAt">When the soonest surviving boost lot dies. Null when no boost remains.</param>
/// <param name="AssistantBoostExpired">Purchased tokens that died unused — shown so the loss is never silent.</param>
/// <param name="AssistantTokensSince">
/// Where <paramref name="AssistantTokensUsed"/> starts counting: the start of the current billing period for a paying
/// account, null for a trial (everything counts) and when locked.
/// </param>
public sealed record AccountAccessDto(
    string State,
    string? LockReason,
    DateTimeOffset? TrialEndsAt,
    int? TrialDaysRemaining,
    int? TrialLengthDays,
    long? AssistantTokensUsed,
    long? AssistantTokenLimit,
    DateTimeOffset? AssistantTokensSince,
    long AssistantBoostRemaining = 0,
    DateTimeOffset? AssistantBoostExpiresAt = null,
    long AssistantBoostExpired = 0);

public sealed class GetAccountAccessHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAccountAccessReader accessReader,
    IAssistantTokenBalanceReader balanceReader,
    IStripeSubscriptionReconciler reconciler,
    IOptions<BillingOptions> billingOptions)
    : IRequestHandler<GetAccountAccessQuery, Result<AccountAccessDto>>
{
    public async Task<Result<AccountAccessDto>> Handle(GetAccountAccessQuery request, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsResolved)
            return Result<AccountAccessDto>.Failure("No tenant for this request.");

        var access = await accessReader.GetAsync(tenantContext.TenantId, cancellationToken);
        if (access is null)
            return Result<AccountAccessDto>.Failure("Tenant not found.");

        // ★★ A PAYMENT IN STRIPE MUST NEVER LEAVE THE CUSTOMER LOCKED (KAN-77). The paywall reads this; before telling a
        // locked account it is locked, ask Stripe whether a live subscription exists that the database missed (a lost
        // webhook).
        //
        // ★★ AND THE MIRROR CASE: PastDue. Found in the wild — a tenant sat PastDue since July with FULL ACCESS on a
        // subscription Stripe no longer had. The rule used to be "only for Locked, an open account costs no Stripe
        // call", which is right for a healthy account and exactly wrong for this one: PastDue keeps access open, so it
        // was never re-checked and could stay that way forever. It is a rare state, so the extra call is cheap, and it
        // is the one open state where the stored row is already known to be in trouble.
        var needsReconcile = access.State == AccountAccessState.Locked
            || await IsPastDueAsync(cancellationToken);

        if (needsReconcile && await reconciler.ReconcileAsync(cancellationToken))
            access = await accessReader.GetAsync(tenantContext.TenantId, cancellationToken) ?? access;

        int? trialLength = access.State == AccountAccessState.Trial ? billingOptions.Value.TrialDays : null;

        // ★★ THE BALANCE COMES FROM THE ONE READER (KAN-83), never assembled here. This response is what the meter on
        // screen draws; the assistant's refusal reads the same object. Recomputing it here is exactly how a screen ends
        // up saying "1.2M of 3M used" next to an assistant that will not answer.
        var balance = await balanceReader.GetAsync(tenantContext.TenantId, cancellationToken, known: access);

        // Where the usage is counted FROM, so the screen can say "this period" honestly. A trial counts everything.
        DateTimeOffset? since = null;
        if (access.State == AccountAccessState.Active)
        {
            var subscription = await db.UserSubscriptions
                .Where(s => s.TenantId == tenantContext.TenantId && s.StripeSubscriptionId != null)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.CurrentPeriodStart, s.CreatedAt })
                .FirstOrDefaultAsync(cancellationToken);

            since = subscription?.CurrentPeriodStart ?? subscription?.CreatedAt;
        }

        return Result<AccountAccessDto>.Success(new AccountAccessDto(
            access.State.ToString(),
            access.LockReason?.ToString(),
            access.TrialEndsAt,
            access.TrialDaysRemaining,
            trialLength,
            balance?.IncludedUsed,
            balance?.IncludedLimit,
            since,
            balance?.BoostRemaining ?? 0,
            balance?.BoostNextExpiry,
            balance?.BoostExpired ?? 0));
    }

    /// <summary>
    /// Whether the stored subscription is behind on payment. Read separately because <see cref="AccountAccess"/>
    /// deliberately folds PastDue into Active — for access it IS active; for reconciliation it is the state worth
    /// re-checking.
    /// </summary>
    private async Task<bool> IsPastDueAsync(CancellationToken cancellationToken) =>
        await db.UserSubscriptions
            .AnyAsync(s => s.TenantId == tenantContext.TenantId
                && s.StripeSubscriptionId != null
                && s.Status == SubscriptionStatus.PastDue, cancellationToken);
}
