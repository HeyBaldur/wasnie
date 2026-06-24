using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Integrations.Crm;

namespace Wasnie.Infrastructure.Services.HubSpot;

/// <summary>
/// HubSpot implementation of <see cref="ICrmDealSource"/> (Phase 2). READ-ONLY: only issues GET/search
/// reads — never writes to HubSpot.
///
/// "Closed-won" is determined by HubSpot's CALCULATED property <c>hs_is_closed_won = true</c>, NOT by a
/// hardcoded stage id. HubSpot maintains this boolean per deal for whichever stage is the won stage in
/// that deal's pipeline, so it tolerates custom pipelines/stages across accounts (per the design doc).
///
/// SECURITY: resolves the tenant's access token via <see cref="IHubSpotTokenProvider"/> and never logs it.
/// Honours 429 rate limiting (Retry-After + exponential backoff) and follows cursor pagination internally.
/// </summary>
public sealed class HubSpotCrmDealSource(
    IHttpClientFactory httpClientFactory,
    IHubSpotTokenProvider tokenProvider,
    IHubSpotOAuthClient oauthClient,
    IOptions<HubSpotOptions> options,
    ILogger<HubSpotCrmDealSource> logger)
    : ICrmDealSource
{
    private readonly HubSpotOptions _opts = options.Value;

    public string SourceName => "HubSpot";

    public async Task<string?> GetDefaultCurrencyAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var accessToken = await tokenProvider.GetValidAccessTokenAsync(tenantId, cancellationToken)
            ?? throw new CrmNotConnectedException(SourceName);

        var info = await oauthClient.GetAccountInfoAsync(accessToken, cancellationToken);
        return NormalizeCurrency(info.CompanyCurrency);
    }

    private static readonly string[] DealProperties =
    [
        "dealname", "amount", "closedate", "hs_is_closed_won", "dealstage",
        "hubspot_owner_id", "deal_currency_code"
    ];

    public Task<IReadOnlyList<CrmDeal>> GetClosedWonDealsAsync(
        Guid tenantId, CancellationToken cancellationToken = default) =>
        ReadClosedWonAsync(tenantId, modifiedSince: null, cancellationToken);

    public Task<IReadOnlyList<CrmDeal>> GetClosedWonDealsModifiedSinceAsync(
        Guid tenantId, DateTimeOffset modifiedSince, CancellationToken cancellationToken = default) =>
        ReadClosedWonAsync(tenantId, modifiedSince, cancellationToken);

    /// <summary>
    /// Shared closed-won reader. When <paramref name="modifiedSince"/> is set it ALSO filters on
    /// <c>hs_lastmodifieddate &gt;= since</c> (incremental Phase-3 polling) and throttles between pages to
    /// stay within HubSpot's 4 req/s Search cap; when null it returns every closed-won deal (full backfill).
    /// </summary>
    private async Task<IReadOnlyList<CrmDeal>> ReadClosedWonAsync(
        Guid tenantId, DateTimeOffset? modifiedSince, CancellationToken cancellationToken)
    {
        var accessToken = await tokenProvider.GetValidAccessTokenAsync(tenantId, cancellationToken)
            ?? throw new CrmNotConnectedException(SourceName);

        var url = $"{_opts.ApiBaseUrl.TrimEnd('/')}{_opts.DealsSearchPath}";
        var deals = new List<CrmDeal>();
        string? after = null;
        var pages = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestBody = BuildSearchBody(after, modifiedSince);
            using var doc = await SendWithRetryAsync(
                () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    return req;
                },
                "deals.search",
                cancellationToken);

            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var dealElement in results.EnumerateArray())
                    deals.Add(MapDeal(dealElement));
            }

            after = ExtractPagingAfter(root);
            pages++;

            if (pages >= _opts.MaxReadPages && after is not null)
            {
                logger.LogWarning(
                    "HubSpot deal read hit the page cap ({MaxPages} pages); returning {Count} deals and stopping. Some closed-won deals may not be included.",
                    _opts.MaxReadPages, deals.Count);
                break;
            }

            // Throttle Search to ≤4 req/s per tenant (only matters when there is a next page to fetch).
            if (after is not null && _opts.SearchThrottleMs > 0)
                await Task.Delay(_opts.SearchThrottleMs, cancellationToken);
        }
        while (after is not null);

        logger.LogInformation(
            "HubSpot closed-won deal read ({Mode}) returned {Count} deals across {Pages} page(s).",
            modifiedSince is null ? "full" : "incremental", deals.Count, pages);
        return deals;
    }

    public async Task<IReadOnlyList<CrmOwner>> GetOwnersAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var accessToken = await tokenProvider.GetValidAccessTokenAsync(tenantId, cancellationToken)
            ?? throw new CrmNotConnectedException(SourceName);

        // Fetch active owners, then archived owners (HubSpot returns them separately). Archived owners
        // may still be a deal's owner; merge so we can show/resolve them too.
        var owners = new Dictionary<string, CrmOwner>(StringComparer.Ordinal);
        await CollectOwnersAsync(accessToken, archived: false, owners, cancellationToken);
        await CollectOwnersAsync(accessToken, archived: true, owners, cancellationToken);

        return owners.Values.ToList();
    }

    private async Task CollectOwnersAsync(
        string accessToken, bool archived, Dictionary<string, CrmOwner> sink, CancellationToken cancellationToken)
    {
        var baseUrl = $"{_opts.ApiBaseUrl.TrimEnd('/')}{_opts.OwnersPath}";
        string? after = null;
        var pages = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = $"{baseUrl}?limit={_opts.ReadPageSize}&archived={(archived ? "true" : "false")}"
                + (after is not null ? $"&after={Uri.EscapeDataString(after)}" : string.Empty);

            using var doc = await SendWithRetryAsync(
                () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    return req;
                },
                archived ? "owners.archived" : "owners",
                cancellationToken);

            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var ownerElement in results.EnumerateArray())
                {
                    var owner = MapOwner(ownerElement, archived);
                    sink[owner.Id] = owner; // active overwrites archived on id collision (active is authoritative)
                }
            }

            after = ExtractPagingAfter(root);
            pages++;

            if (pages >= _opts.MaxReadPages && after is not null)
            {
                logger.LogWarning("HubSpot owner read ({Kind}) hit the page cap ({MaxPages}); stopping early.",
                    archived ? "archived" : "active", _opts.MaxReadPages);
                break;
            }
        }
        while (after is not null);
    }

    private string BuildSearchBody(string? after, DateTimeOffset? modifiedSince)
    {
        // CRM Search API body: filter on the calculated hs_is_closed_won boolean. Filters within one
        // filterGroup are AND-ed, so adding hs_lastmodifieddate GTE narrows to "closed-won AND changed since".
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("filterGroups");
            writer.WriteStartObject();
            writer.WriteStartArray("filters");
            writer.WriteStartObject();
            writer.WriteString("propertyName", "hs_is_closed_won");
            writer.WriteString("operator", "EQ");
            writer.WriteString("value", "true");
            writer.WriteEndObject();
            if (modifiedSince is { } since)
            {
                writer.WriteStartObject();
                writer.WriteString("propertyName", "hs_lastmodifieddate");
                writer.WriteString("operator", "GTE");
                // HubSpot datetime search values are epoch milliseconds (UTC).
                writer.WriteString("value", since.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            // Sort by last-modified ascending so paging is stable across the incremental window.
            writer.WriteStartArray("sorts");
            writer.WriteStartObject();
            writer.WriteString("propertyName", "hs_lastmodifieddate");
            writer.WriteString("direction", "ASCENDING");
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartArray("properties");
            foreach (var p in DealProperties)
                writer.WriteStringValue(p);
            writer.WriteEndArray();

            writer.WriteNumber("limit", _opts.ReadPageSize);
            if (!string.IsNullOrEmpty(after))
                writer.WriteString("after", after);

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static CrmDeal MapDeal(JsonElement deal)
    {
        var id = GetString(deal, "id") ?? string.Empty;
        var props = deal.TryGetProperty("properties", out var p) ? p : default;

        return new CrmDeal(
            Id: id,
            Name: GetString(props, "dealname"),
            Amount: ParseDecimal(GetString(props, "amount")),
            CurrencyCode: NormalizeCurrency(GetString(props, "deal_currency_code")),
            CloseDate: ParseDate(GetString(props, "closedate")),
            OwnerId: NullIfBlank(GetString(props, "hubspot_owner_id")));
    }

    private static CrmOwner MapOwner(JsonElement owner, bool archived) =>
        new(
            Id: GetString(owner, "id") ?? string.Empty,
            Email: NullIfBlank(GetString(owner, "email")),
            FirstName: NullIfBlank(GetString(owner, "firstName")),
            LastName: NullIfBlank(GetString(owner, "lastName")),
            Archived: owner.TryGetProperty("archived", out var a) && a.ValueKind == JsonValueKind.True || archived);

    /// <summary>
    /// Sends a request built by <paramref name="requestFactory"/> (a factory, so each retry gets a fresh
    /// HttpRequestMessage), retrying on HTTP 429 using Retry-After when present, else exponential backoff.
    /// </summary>
    private async Task<JsonDocument> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory, string operation, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("HubSpot");

        for (var attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < _opts.RateLimitMaxRetries)
            {
                var delay = ResolveRetryDelay(response, attempt);
                logger.LogWarning(
                    "HubSpot {Operation} rate-limited (429); retry {Attempt}/{Max} after {Delay}s.",
                    operation, attempt + 1, _opts.RateLimitMaxRetries, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("HubSpot {Operation} failed: {Status}.", operation, (int)response.StatusCode);
                throw new InvalidOperationException($"HubSpot {operation} call failed ({(int)response.StatusCode}).");
            }

            return JsonDocument.Parse(body);
        }
    }

    private static TimeSpan ResolveRetryDelay(HttpResponseMessage response, int attempt)
    {
        // Prefer the server's Retry-After (seconds); fall back to capped exponential backoff.
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds)
            && seconds is > 0 and <= 60)
            return TimeSpan.FromSeconds(seconds);

        var backoff = Math.Min(30, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(backoff);
    }

    private static string? ExtractPagingAfter(JsonElement root) =>
        root.TryGetProperty("paging", out var paging)
        && paging.TryGetProperty("next", out var next)
        && next.TryGetProperty("after", out var after)
        && after.ValueKind == JsonValueKind.String
            ? after.GetString()
            : null;

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static decimal? ParseDecimal(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? NormalizeCurrency(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToUpperInvariant();

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // HubSpot v3 datetime properties are ISO 8601 UTC (e.g. "2026-06-20T00:00:00Z").
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return DateOnly.FromDateTime(dto.UtcDateTime);

        // Defensive fallback: some exports return epoch milliseconds.
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochMillis))
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(epochMillis).UtcDateTime);

        return null;
    }
}
