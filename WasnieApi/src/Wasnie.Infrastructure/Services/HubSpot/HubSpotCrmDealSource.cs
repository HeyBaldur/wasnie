using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    IApplicationDbContext db,
    ILogger<HubSpotCrmDealSource> logger)
    : ICrmDealSource
{
    private readonly HubSpotOptions _opts = options.Value;

    /// <summary>
    /// WI-CRM-CATEGORY: the tenant-declared HubSpot property whose value feeds Category, or null when the
    /// tenant has not configured one (feature off). Tenant-explicit (IgnoreQueryFilters) so it resolves the
    /// same under the background sync job and an authenticated request, exactly like the token lookup.
    /// </summary>
    private async Task<string?> GetConfiguredCategoryPropertyAsync(Guid tenantId, CancellationToken ct)
    {
        var name = await db.HubSpotConnections
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .Select(c => c.CategoryPropertyName)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

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

        // WI-CRM-CATEGORY: load the tenant's configured category property once per read (null = feature off).
        var categoryProp = await GetConfiguredCategoryPropertyAsync(tenantId, cancellationToken);

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

        var dealsWithLines = await AttachLineItemsAsync(deals, accessToken, categoryProp, cancellationToken);

        logger.LogInformation(
            "HubSpot closed-won deal read ({Mode}) returned {Count} deals across {Pages} page(s).",
            modifiedSince is null ? "full" : "incremental", dealsWithLines.Count, pages);
        return dealsWithLines;
    }

    // hs_sku is a DEFAULT line item property and travels in the batch-read below — no extra API call
    // and nothing for the customer to configure. "Product type" (Inventory/Non-Inventory/Service) is
    // deliberately NOT requested: HubSpot's API reference does not publish its internal name, and
    // asking for a property that does not exist is ignored silently, which would look like a tenant
    // with no data rather than a wrong request.
    private static readonly string[] LineItemProperties = ["quantity", "price", "amount", "name", "hs_sku"];

    /// <summary>
    /// WI-CRM-CATEGORY: the line-item properties to request — the defaults, PLUS the tenant's configured
    /// category property but ONLY when one is configured. Extracted so the "added only when configured"
    /// rule is unit-testable without the HTTP boundary.
    /// </summary>
    internal static IReadOnlyList<string> BuildLineItemProperties(string? categoryProp) =>
        string.IsNullOrWhiteSpace(categoryProp)
            ? LineItemProperties
            : [.. LineItemProperties, categoryProp.Trim()];

    /// <summary>
    /// For each deal, fetch its line items (v4 associations batch-read → line items batch-read) and attach
    /// them. Chunked at 100 per HubSpot batch and throttled like the deal read. Deals with no line items are
    /// returned unchanged (empty <see cref="CrmDeal.Lines"/>). Requires the crm.objects.line_items.read scope.
    /// </summary>
    private async Task<IReadOnlyList<CrmDeal>> AttachLineItemsAsync(
        List<CrmDeal> deals, string accessToken, string? categoryProp, CancellationToken cancellationToken)
    {
        if (deals.Count == 0) return deals;

        // WI-CRM-CATEGORY: request the tenant's configured category property alongside the defaults, ONLY
        // when configured. If it is not a real HubSpot property, HubSpot silently omits it (no error) — the
        // value just comes back empty, which we detect below.
        var lineItemProps = BuildLineItemProperties(categoryProp);

        // 1) deal id → line item ids (v4 associations batch read).
        var lineItemIdsByDeal = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var assocUrl = $"{_opts.ApiBaseUrl.TrimEnd('/')}{_opts.DealLineItemAssociationsPath}";
        var dealIds = deals.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

        foreach (var chunk in dealIds.Chunk(100))
        {
            using var doc = await SendWithRetryAsync(
                () => PostJson(assocUrl, BuildIdInputsBody(chunk), accessToken), "line_items.associations", cancellationToken);

            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in results.EnumerateArray())
                {
                    var fromId = r.TryGetProperty("from", out var f) && f.TryGetProperty("id", out var fid)
                        ? fid.GetString() : null;
                    if (string.IsNullOrWhiteSpace(fromId)) continue;

                    var ids = new List<string>();
                    if (r.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Array)
                        foreach (var t in to.EnumerateArray())
                        {
                            var liId = t.TryGetProperty("toObjectId", out var oid) ? RawId(oid)
                                : t.TryGetProperty("id", out var idv) ? RawId(idv) : null;
                            if (!string.IsNullOrWhiteSpace(liId)) ids.Add(liId!);
                        }
                    if (ids.Count > 0) lineItemIdsByDeal[fromId!] = ids;
                }
            }

            if (_opts.SearchThrottleMs > 0) await Task.Delay(_opts.SearchThrottleMs, cancellationToken);
        }

        if (lineItemIdsByDeal.Count == 0) return deals;

        // 2) line item id → CrmLineItem (batch read with quantity/price/amount/name).
        var lineItemById = new Dictionary<string, CrmLineItem>(StringComparer.Ordinal);
        var liUrl = $"{_opts.ApiBaseUrl.TrimEnd('/')}{_opts.LineItemsBatchReadPath}";
        var allLineItemIds = lineItemIdsByDeal.Values.SelectMany(v => v).Distinct().ToList();

        foreach (var chunk in allLineItemIds.Chunk(100))
        {
            using var doc = await SendWithRetryAsync(
                () => PostJson(liUrl, BuildLineItemsReadBody(chunk, lineItemProps), accessToken), "line_items.read", cancellationToken);

            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    var id = GetString(el, "id");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var props = el.TryGetProperty("properties", out var p) ? p : default;
                    lineItemById[id!] = new CrmLineItem(
                        Id: id!,
                        Name: NullIfBlank(GetString(props, "name")),
                        Quantity: ParseDecimal(GetString(props, "quantity")),
                        Price: ParseDecimal(GetString(props, "price")),
                        Amount: ParseDecimal(GetString(props, "amount")),
                        Sku: NullIfBlank(GetString(props, "hs_sku")),
                        CategoryFromCrm: categoryProp is null ? null : NullIfBlank(GetString(props, categoryProp)));
                }
            }

            if (_opts.SearchThrottleMs > 0) await Task.Delay(_opts.SearchThrottleMs, cancellationToken);
        }

        // WI-CRM-CATEGORY — HubSpot's silence trap: if the tenant configured a category property that does
        // NOT exist in their CRM (typo / never created), HubSpot omits it with no error and every value
        // arrives blank, so categories would look mysteriously empty. Surface it as a visible warning
        // instead. NEVER fails the sync — the deals still import and enrichment falls back to the lookup
        // table; this only tells the admin to fix the property name.
        if (categoryProp is not null && lineItemById.Count > 0 &&
            lineItemById.Values.All(li => li.CategoryFromCrm is null))
        {
            logger.LogWarning(
                "HubSpot category property '{Property}' came back empty on ALL {Count} line item(s) in this " +
                "sync. Either it is not a real property in HubSpot (check the exact internal name in " +
                "Settings → Properties) or no products have a value set. Transactions fall back to the manual " +
                "category table until this is fixed.",
                categoryProp, lineItemById.Count);
        }

        // 3) attach line items to their deals (deals without associations are returned unchanged).
        var withLines = new List<CrmDeal>(deals.Count);
        foreach (var d in deals)
        {
            if (lineItemIdsByDeal.TryGetValue(d.Id, out var liIds))
            {
                var lines = liIds
                    .Where(lineItemById.ContainsKey)
                    .Select(id => lineItemById[id])
                    .ToList();
                withLines.Add(d with { LineItems = lines });
            }
            else
            {
                withLines.Add(d);
            }
        }
        return withLines;
    }

    private static HttpRequestMessage PostJson(string url, string body, string accessToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return req;
    }

    // {"inputs":[{"id":"..."}, ...]}
    private static string BuildIdInputsBody(IEnumerable<string> ids)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("inputs");
            foreach (var id in ids)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // {"properties":[...],"inputs":[{"id":"..."}, ...]}
    private static string BuildLineItemsReadBody(IEnumerable<string> ids, IReadOnlyList<string> properties)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("properties");
            foreach (var p in properties)
                writer.WriteStringValue(p);
            writer.WriteEndArray();
            writer.WriteStartArray("inputs");
            foreach (var id in ids)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? RawId(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };

    public async Task<IReadOnlyList<CrmDealStatus>> GetDealStatusesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<string> dealIds, CancellationToken cancellationToken = default)
    {
        var ids = dealIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return [];

        var accessToken = await tokenProvider.GetValidAccessTokenAsync(tenantId, cancellationToken)
            ?? throw new CrmNotConnectedException(SourceName);

        var url = $"{_opts.ApiBaseUrl.TrimEnd('/')}{_opts.DealsBatchReadPath}";
        var statuses = new List<CrmDealStatus>(ids.Count);

        // Batch-read by id (≤100 per call). The batch-read endpoint returns ONLY the ids it finds — a lost
        // deal is still returned (with hs_is_closed_won=false); a deleted/archived/no-access id is omitted,
        // and the reverse reconciler treats that absence conservatively (never as "lost").
        foreach (var chunk in ids.Chunk(100))
        {
            using var doc = await SendWithRetryAsync(
                () => PostJson(url, BuildDealsBatchReadBody(chunk), accessToken), "deals.batch_read", cancellationToken);

            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    var id = GetString(el, "id");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var props = el.TryGetProperty("properties", out var p) ? p : default;
                    // HubSpot returns the calculated boolean as the string "true"/"false".
                    var isWon = string.Equals(GetString(props, "hs_is_closed_won"), "true", StringComparison.OrdinalIgnoreCase);
                    statuses.Add(new CrmDealStatus(id!, isWon));
                }
            }

            if (_opts.SearchThrottleMs > 0) await Task.Delay(_opts.SearchThrottleMs, cancellationToken);
        }

        return statuses;
    }

    // {"properties":["hs_is_closed_won"],"inputs":[{"id":"..."}, ...]}
    private static string BuildDealsBatchReadBody(IEnumerable<string> ids)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("properties");
            writer.WriteStringValue("hs_is_closed_won");
            writer.WriteStringValue("dealstage");
            writer.WriteEndArray();
            writer.WriteStartArray("inputs");
            foreach (var id in ids)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
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
