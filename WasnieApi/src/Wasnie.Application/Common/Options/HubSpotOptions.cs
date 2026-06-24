namespace Wasnie.Application.Common.Options;

/// <summary>
/// Configuration for the HubSpot OAuth Public App integration (Phase 1).
///
/// SECRETS (ClientId, ClientSecret, TokenEncryptionKey) live ONLY in gitignored config
/// (appsettings.Development.json for dev, environment variables / a secret store for prod) — never in
/// committed appsettings.json and never hardcoded. Dev and prod use SEPARATE HubSpot apps, so each
/// environment supplies its own ClientId/ClientSecret via its own config file.
/// </summary>
public sealed class HubSpotOptions
{
    public const string SectionName = "HubSpot";

    /// <summary>HubSpot Public App client id (from the HubSpot developer portal). Secret-ish — keep in gitignored config.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>HubSpot Public App client secret. SECRET — gitignored config only.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// The exact redirect/callback URL registered in the HubSpot app. Must match what the app sends.
    /// Dev default: http://localhost:5091/api/integrations/hubspot/callback
    /// </summary>
    public string RedirectUri { get; init; } = "http://localhost:5091/api/integrations/hubspot/callback";

    /// <summary>
    /// Space-separated OAuth scopes requested at authorization. Keep MINIMAL and EXACTLY matching the
    /// scopes configured on the HubSpot app (HubSpot rejects the install if they differ). Default is the
    /// read set needed by later phases (deals, owners, pipelines/schemas) so the user doesn't re-consent.
    /// NOTE: do NOT include "oauth" — it is not a HubSpot scope and breaks the scope match.
    /// </summary>
    public string Scopes { get; init; } = "crm.objects.deals.read crm.objects.owners.read crm.schemas.deals.read";

    /// <summary>HubSpot authorization URL (where the user is sent to grant access).</summary>
    public string AuthorizationEndpoint { get; init; } = "https://app.hubspot.com/oauth/authorize";

    /// <summary>HubSpot OAuth token endpoint (code→token and refresh).</summary>
    public string TokenEndpoint { get; init; } = "https://api.hubapi.com/oauth/v1/token";

    /// <summary>Base URL for HubSpot API calls (token-info, account-info, etc.).</summary>
    public string ApiBaseUrl { get; init; } = "https://api.hubapi.com";

    /// <summary>
    /// Base64-encoded 32-byte (256-bit) key used to encrypt tokens at rest (AES-256-GCM).
    /// SECRET — gitignored config only. PRODUCTION should source this from a KMS / envelope encryption
    /// rather than a static config value (see AesTokenEncryptionService).
    /// </summary>
    public string TokenEncryptionKey { get; init; } = string.Empty;

    /// <summary>Where to send the browser after the OAuth callback completes (the integrations UI).</summary>
    public string FrontendReturnUrl { get; init; } = "http://localhost:4200/integrations";

    /// <summary>
    /// Path (relative to <see cref="ApiBaseUrl"/>) of the CRM deals SEARCH endpoint used in Phase 2 to
    /// pull closed-won deals with server-side filtering + cursor pagination. Versioned so it can change
    /// without code edits (HubSpot keeps /crm/v3 alive alongside the date-versioned endpoints).
    /// </summary>
    public string DealsSearchPath { get; init; } = "/crm/v3/objects/deals/search";

    /// <summary>Path (relative to <see cref="ApiBaseUrl"/>) of the owners list endpoint (Phase 2).</summary>
    public string OwnersPath { get; init; } = "/crm/v3/owners";

    /// <summary>Page size for deal/owner reads (HubSpot caps the search API at 100 per page).</summary>
    public int ReadPageSize { get; init; } = 100;

    /// <summary>
    /// Safety cap on the number of pages a single deal/owner read will follow, so a pathological cursor
    /// can never loop forever. At 100/page this is 50k deals — far above any expected Phase 2 volume.
    /// If hit, the read logs a warning (no silent truncation) and returns what it has.
    /// </summary>
    public int MaxReadPages { get; init; } = 500;

    /// <summary>How many times to retry a HubSpot read after a 429 (rate limit) before giving up.</summary>
    public int RateLimitMaxRetries { get; init; } = 5;

    /// <summary>
    /// Minimum gap between consecutive CRM Search requests for one tenant, in milliseconds. HubSpot's
    /// Search API is capped at 4 requests/second PER account, so the incremental polling read waits at
    /// least this long between pages (250ms ⇒ ≤4/s). Proactive throttle so we never approach the 429.
    /// </summary>
    public int SearchThrottleMs { get; init; } = 250;

    /// <summary>Refresh the access token when it is within this many seconds of expiry.</summary>
    public int RefreshSkewSeconds { get; init; } = 300;

    /// <summary>How long an OAuth <c>state</c> is valid before the handshake must be restarted.</summary>
    public int StateTtlMinutes { get; init; } = 10;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(TokenEncryptionKey);
}
