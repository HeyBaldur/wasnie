namespace Wasnie.Application.Features.Subscription.DTOs;

/// <summary>
/// What "Manage billing" shows about the Stripe customer: the payment method and the invoice history.
/// Empty (null method, no invoices) when the tenant has no Stripe customer yet — a trial is not an error.
/// <c>Synced</c> is true when reading Stripe found the stored subscription out of date and corrected it: the client
/// must reload the account's access, which may have changed (a missed cancellation now locks the account).
/// </summary>
public sealed record BillingDetailsDto(
    BillingSubscriptionDto? Subscription,
    BillingPaymentMethodDto? PaymentMethod,
    IReadOnlyList<BillingInvoiceDto> Invoices,
    bool Synced = false);

/// <summary>Live from Stripe. Status is Stripe's raw value (active, past_due, canceled…); the UI maps it by whitelist.</summary>
public sealed record BillingSubscriptionDto(
    string Status,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelAt,
    DateTimeOffset? EndedAt);

public sealed record BillingPaymentMethodDto(
    string Type,
    string? Brand,
    string? Last4,
    long? ExpMonth,
    long? ExpYear);

/// <summary>Status is Stripe's raw invoice status (draft, open, paid, uncollectible, void); the UI maps it by whitelist.</summary>
public sealed record BillingInvoiceDto(
    string Id,
    string? Number,
    string? Description,
    DateTimeOffset CreatedAt,
    decimal Total,
    string Currency,
    string? Status,
    string? HostedInvoiceUrl,
    string? InvoicePdfUrl);
