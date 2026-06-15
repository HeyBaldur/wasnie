namespace Wasnie.Application.Common.Options;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; init; } = string.Empty;
    public string PublishableKey { get; init; } = string.Empty;

    // Maps Stripe ProductId (prod_...) → Wasnie Tier name (e.g. "Starter").
    // Used when product.Metadata["tier"] is absent (the common case for existing products).
    // Precedence: metadata["tier"] → this map → discard with WARNING.
    public Dictionary<string, string> ProductTierMap { get; init; } = new();

    // Webhook signing secret (whsec_...) — used to verify Stripe webhook signatures.
    // Never hardcode; set in appsettings.Development.json (dev) or env var (prod).
    public string WebhookSecret { get; init; } = string.Empty;

    // Base URL of the frontend app (no trailing slash). Used to build Stripe redirect URLs.
    public string FrontendBaseUrl { get; init; } = "http://localhost:4200";
}
