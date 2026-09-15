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
/// <param name="AssistantTokenLimit">The trial's token allowance. Null for a paying account, which has none.</param>
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
    DateTimeOffset? AssistantTokensSince);

public sealed class GetAccountAccessHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAccountAccessReader accessReader,
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
        // webhook). Only for Locked — an open account costs no Stripe call.
        if (access.State == AccountAccessState.Locked && await reconciler.ReconcileAsync(cancellationToken))
            access = await accessReader.GetAsync(tenantContext.TenantId, cancellationToken) ?? access;

        long? used = null;
        long? limit = null;
        DateTimeOffset? since = null;
        int? trialLength = null;
        if (access.State == AccountAccessState.Trial)
        {
            limit = billingOptions.Value.TrialAssistantTokenLimit;
            trialLength = billingOptions.Value.TrialDays;
            used = await Wasnie.Application.Assistant.Common.AssistantTokenMeter.UsedAsync(
                db, tenantContext.TenantId, since: null, cancellationToken);
        }
        else if (access.State == AccountAccessState.Active)
        {
            // ★ THE CURRENT BILLING PERIOD, AS DECIDED FOR KAN-80. A paying account sees what it consumed this period and
            // nothing older. The period comes from the Stripe subscription; when Stripe has not told us its start yet,
            // the subscription's own creation is the honest fallback — it is when this paid relationship began.
            var subscription = await db.UserSubscriptions
                .Where(s => s.TenantId == tenantContext.TenantId && s.StripeSubscriptionId != null)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.CurrentPeriodStart, s.CreatedAt })
                .FirstOrDefaultAsync(cancellationToken);

            since = subscription?.CurrentPeriodStart ?? subscription?.CreatedAt;
            used = await Wasnie.Application.Assistant.Common.AssistantTokenMeter.UsedAsync(
                db, tenantContext.TenantId, since, cancellationToken);
        }

        return Result<AccountAccessDto>.Success(new AccountAccessDto(
            access.State.ToString(),
            access.LockReason?.ToString(),
            access.TrialEndsAt,
            access.TrialDaysRemaining,
            trialLength,
            used,
            limit,
            since));
    }
}
