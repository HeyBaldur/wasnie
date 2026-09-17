using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Application.Features.Subscription.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

/// <summary>
/// The card on file and the invoice history for "Manage billing", read live from Stripe (we store neither).
/// Gated by Subscription.Manage: invoices carry amounts and links to the customer's billing documents.
///
/// ★ READING STRIPE IS ALSO THE CHANCE TO NOTICE A MISSED WEBHOOK. When the live subscription differs from the stored
/// row, the row is corrected with the webhook's own rules (<see cref="IStripeSubscriptionReconciler"/>). The row decides
/// access, so leaving it stale would keep a cancelled account open — and make Pricing contradict this page.
/// </summary>
public sealed class GetBillingDetailsHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAuthorizationService authorization,
    IStripeBillingDetailsReader stripe,
    IStripeSubscriptionReconciler reconciler,
    ILogger<GetBillingDetailsHandler> logger)
    : IRequestHandler<GetBillingDetailsQuery, Result<BillingDetailsDto>>
{
    /// <summary>The page shows the recent history; older invoices remain in the Stripe billing portal.</summary>
    public const int InvoiceLimit = 12;

    public async Task<Result<BillingDetailsDto>> Handle(GetBillingDetailsQuery request, CancellationToken cancellationToken)
    {
        await authorization.RequireAsync(Permission.SubscriptionManage, cancellationToken);

        var subscription = await db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);

        if (subscription is null || string.IsNullOrEmpty(subscription.StripeCustomerId))
            return Result<BillingDetailsDto>.Success(new BillingDetailsDto(null, null, []));

        try
        {
            var details = await stripe.GetAsync(
                subscription.StripeCustomerId,
                subscription.StripeSubscriptionId,
                InvoiceLimit,
                cancellationToken);

            var method = details.PaymentMethod is { } pm
                ? new BillingPaymentMethodDto(pm.Type, pm.Brand, pm.Last4, pm.ExpMonth, pm.ExpYear)
                : null;

            var live = details.Subscription is { } s
                ? new BillingSubscriptionDto(s.Status, Utc(s.CurrentPeriodStart), Utc(s.CurrentPeriodEnd), s.CancelAtPeriodEnd, Utc(s.CancelAt), Utc(s.EndedAt))
                : null;

            var invoices = MapInvoices(details);

            // Drift on the stored subscription, or a stored subscription that is over (a newer one may exist).
            var synced = false;
            if (details.Subscription is null
                || StripeSubscriptionStatus.IsEnded(details.Subscription.Status)
                || StripeSubscriptionDrift.Differs(subscription, details.Subscription))
            {
                synced = await reconciler.ReconcileAsync(cancellationToken);
            }

            if (synced)
            {
                // The row may now hold a different subscription: read Stripe again for THAT one.
                var fresh = await db.UserSubscriptions.AsNoTracking()
                    .FirstAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);
                details = await stripe.GetAsync(fresh.StripeCustomerId!, fresh.StripeSubscriptionId, InvoiceLimit, cancellationToken);
                live = details.Subscription is { } s2
                    ? new BillingSubscriptionDto(s2.Status, Utc(s2.CurrentPeriodStart), Utc(s2.CurrentPeriodEnd), s2.CancelAtPeriodEnd, Utc(s2.CancelAt), Utc(s2.EndedAt))
                    : null;
                method = details.PaymentMethod is { } pm2
                    ? new BillingPaymentMethodDto(pm2.Type, pm2.Brand, pm2.Last4, pm2.ExpMonth, pm2.ExpYear)
                    : null;
                invoices = MapInvoices(details);
            }

            return Result<BillingDetailsDto>.Success(new BillingDetailsDto(live, method, invoices, synced));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to read Stripe billing details for tenant {TenantId}", tenantContext.TenantId);
            return Result<BillingDetailsDto>.Failure("Failed to load billing details. Please try again.");
        }
    }

    private static List<BillingInvoiceDto> MapInvoices(StripeBillingDetails details) => details.Invoices
        .Select(i => new BillingInvoiceDto(
            i.Id,
            i.Number,
            i.Description,
            Utc(i.CreatedAt)!.Value,
            StripeAmount.FromMinorUnits(i.TotalMinorUnits, i.Currency),
            i.Currency.ToUpperInvariant(),
            i.Status,
            i.HostedInvoiceUrl,
            i.InvoicePdfUrl,
            i.NextPaymentAttempt,
            i.AttemptCount))
        .ToList();

    /// <summary>Stripe.net returns UTC DateTimes with Kind unspecified.</summary>
    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
}
