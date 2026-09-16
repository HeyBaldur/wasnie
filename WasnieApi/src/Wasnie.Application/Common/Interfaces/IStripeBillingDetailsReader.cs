namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Read-only view of what Stripe holds for a customer: the card on file and the recent invoices. Nothing here writes
/// to Stripe — changing the card, cancelling or paying stays in the Stripe billing portal.
/// Throws StripeException on network/API failures.
/// </summary>
public interface IStripeBillingDetailsReader
{
    Task<StripeBillingDetails> GetAsync(
        string customerId,
        string? subscriptionId,
        int invoiceLimit,
        CancellationToken cancellationToken = default);
}

/// <summary>Amounts are still in Stripe's minor units; <see cref="Features.Subscription.StripeAmount"/> converts them.</summary>
public sealed record StripeBillingDetails(
    StripeSubscriptionSnapshot? Subscription,
    StripePaymentMethodSummary? PaymentMethod,
    IReadOnlyList<StripeInvoiceSummary> Invoices);

/// <summary>
/// The subscription as Stripe has it NOW. The local row is only as fresh as the last webhook that reached us; a
/// missed renewal leaves it on an expired period (KAN-77, runtime: "Renews on Aug 24 · 0 days left" in September).
/// </summary>
public sealed record StripeSubscriptionSnapshot(
    string Status,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTime? CancelAt,
    DateTime? EndedAt);

public sealed record StripePaymentMethodSummary(
    string Type,
    string? Brand,
    string? Last4,
    long? ExpMonth,
    long? ExpYear);

public sealed record StripeInvoiceSummary(
    string Id,
    string? Number,
    string? Description,
    DateTime CreatedAt,
    long TotalMinorUnits,
    string Currency,
    string? Status,
    string? HostedInvoiceUrl,
    string? InvoicePdfUrl);
