namespace Wasnie.Application.Common.Options;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; init; } = string.Empty;
    public string PublishableKey { get; init; } = string.Empty;

    // KAN-77: the old ProductTierMap is gone. Which Stripe product is which plan now lives in the plan
    // catalog (Billing:Plans / product metadata.plan) — see SubscriptionPlanCatalog.

    // Webhook signing secret (whsec_...) — used to verify Stripe webhook signatures.
    // Never hardcode; set in appsettings.Development.json (dev) or env var (prod).
    public string WebhookSecret { get; init; } = string.Empty;

    // Base URL of the frontend app (no trailing slash). Used to build Stripe redirect URLs.
    public string FrontendBaseUrl { get; init; } = "http://localhost:4200";
}
