using Microsoft.Extensions.Options;
using Stripe;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;

namespace Wasnie.Infrastructure.Services;

/// <summary>Read-only: GET calls only (customer, subscription, payment methods, invoices).</summary>
public sealed class StripeBillingDetailsReader(IOptions<StripeOptions> options) : IStripeBillingDetailsReader
{
    public async Task<StripeBillingDetails> GetAsync(
        string customerId,
        string? subscriptionId,
        int invoiceLimit,
        CancellationToken cancellationToken = default)
    {
        var client = new StripeClient(options.Value.SecretKey);

        Subscription? subscription = null;
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            try
            {
                subscription = await new SubscriptionService(client).GetAsync(
                    subscriptionId,
                    new SubscriptionGetOptions { Expand = ["default_payment_method"] },
                    cancellationToken: cancellationToken);
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                // The local row points at a subscription Stripe does not have (KAN-77 Paso 0 found two). The card and
                // the invoices belong to the CUSTOMER and are still real; only the live period is unknown.
            }
        }

        var paymentMethod = await ResolvePaymentMethodAsync(client, customerId, subscription, cancellationToken);

        var invoices = await new InvoiceService(client).ListAsync(
            new InvoiceListOptions { Customer = customerId, Limit = invoiceLimit },
            cancellationToken: cancellationToken);

        var summaries = invoices.Data
            // A draft is not a bill yet: Stripe has not finalised its number or amount.
            .Where(i => i.Status != "draft")
            .Select(i => new StripeInvoiceSummary(
                i.Id,
                i.Number,
                i.Lines?.Data?.FirstOrDefault()?.Description,
                i.Created,
                i.Total,
                i.Currency,
                i.Status,
                i.HostedInvoiceUrl,
                i.InvoicePdf,
                i.NextPaymentAttempt,
                i.AttemptCount))
            .ToList();

        return new StripeBillingDetails(Snapshot(subscription), Summarise(paymentMethod), summaries);
    }

    /// <summary>
    /// Checkout stores the card on the SUBSCRIPTION, the portal may store it on the CUSTOMER's invoice settings, and
    /// an older customer may only have an attached card. Stripe charges in that order, so that is the one shown.
    /// </summary>
    private static async Task<PaymentMethod?> ResolvePaymentMethodAsync(
        StripeClient client,
        string customerId,
        Subscription? subscription,
        CancellationToken cancellationToken)
    {
        if (subscription?.DefaultPaymentMethod is not null)
            return subscription.DefaultPaymentMethod;

        var customer = await new CustomerService(client).GetAsync(
            customerId,
            new CustomerGetOptions { Expand = ["invoice_settings.default_payment_method"] },
            cancellationToken: cancellationToken);
        if (customer.InvoiceSettings?.DefaultPaymentMethod is not null)
            return customer.InvoiceSettings.DefaultPaymentMethod;

        var attached = await new CustomerPaymentMethodService(client).ListAsync(
            customerId,
            new CustomerPaymentMethodListOptions { Limit = 1 },
            cancellationToken: cancellationToken);
        return attached.Data.FirstOrDefault();
    }

    /// <summary>
    /// Since API 2025-03-31 the billing period lives on the subscription ITEM, not on the subscription. One plan per
    /// subscription, so the first item is the period.
    /// </summary>
    private static StripeSubscriptionSnapshot? Snapshot(Subscription? subscription)
    {
        if (subscription is null)
            return null;
        var item = subscription.Items?.Data?.FirstOrDefault();
        return new StripeSubscriptionSnapshot(
            subscription.Status,
            item?.CurrentPeriodStart,
            item?.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.CancelAt,
            subscription.EndedAt);
    }

    private static StripePaymentMethodSummary? Summarise(PaymentMethod? pm) => pm switch
    {
        null => null,
        { Card: { } card } => new StripePaymentMethodSummary(pm.Type, card.Brand, card.Last4, card.ExpMonth, card.ExpYear),
        { SepaDebit: { } sepa } => new StripePaymentMethodSummary(pm.Type, null, sepa.Last4, null, null),
        _ => new StripePaymentMethodSummary(pm.Type, null, null, null, null),
    };
}
