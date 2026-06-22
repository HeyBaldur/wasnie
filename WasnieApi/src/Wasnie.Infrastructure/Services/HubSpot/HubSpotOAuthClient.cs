using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Integrations.HubSpot;

namespace Wasnie.Infrastructure.Services.HubSpot;

/// <summary>
/// HTTP client over HubSpot's OAuth + account endpoints.
/// SECURITY: never logs token material — only request shapes, status codes and HubSpot error codes.
/// Token endpoint format: POST {TokenEndpoint} (application/x-www-form-urlencoded). Confirmed current
/// (2026): https://api.hubapi.com/oauth/v1/token. Portal id via /oauth/v1/access-tokens/{token};
/// account info via /account-info/v3/details.
/// </summary>
public sealed class HubSpotOAuthClient(
    IHttpClientFactory httpClientFactory,
    IOptions<HubSpotOptions> options,
    ILogger<HubSpotOAuthClient> logger)
    : IHubSpotOAuthClient
{
    private readonly HubSpotOptions _opts = options.Value;

    public async Task<HubSpotTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["redirect_uri"] = _opts.RedirectUri,
            ["code"] = code,
        };

        var client = httpClientFactory.CreateClient("HubSpot");
        using var response = await client.PostAsync(_opts.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("HubSpot code exchange failed: {Status} {Code}",
                (int)response.StatusCode, ExtractErrorCode(body));
            throw new InvalidOperationException("HubSpot code exchange failed.");
        }

        return ParseTokenResponse(body);
    }

    public async Task<HubSpotRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["redirect_uri"] = _opts.RedirectUri,
            ["refresh_token"] = refreshToken,
        };

        var client = httpClientFactory.CreateClient("HubSpot");
        using var response = await client.PostAsync(_opts.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
            return HubSpotRefreshResult.Ok(ParseTokenResponse(body));

        var errorCode = ExtractErrorCode(body);
        logger.LogWarning("HubSpot token refresh failed: {Status} {Code}", (int)response.StatusCode, errorCode);

        // A bad/expired/revoked refresh token is irrecoverable → caller moves to NeedsReconnect (no retry loop).
        var isInvalid = body.Contains("BAD_REFRESH_TOKEN", StringComparison.OrdinalIgnoreCase)
            || (int)response.StatusCode == 400
            || (int)response.StatusCode == 403;

        return isInvalid
            ? HubSpotRefreshResult.Invalid(errorCode)
            : HubSpotRefreshResult.TransientFailure(errorCode);
    }

    public async Task<long> GetPortalIdAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        // The token-info endpoint takes the token in the path (no Authorization header) and returns hub_id.
        var client = httpClientFactory.CreateClient("HubSpot");
        var url = $"{_opts.ApiBaseUrl.TrimEnd('/')}/oauth/v1/access-tokens/{Uri.EscapeDataString(accessToken)}";
        using var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("HubSpot token-info call failed: {Status}", (int)response.StatusCode);
            throw new InvalidOperationException("Could not resolve the HubSpot portal id.");
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("hub_id", out var hubId) && hubId.TryGetInt64(out var id))
            return id;

        throw new InvalidOperationException("HubSpot token-info response did not include hub_id.");
    }

    public async Task<HubSpotAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("HubSpot");
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_opts.ApiBaseUrl.TrimEnd('/')}/account-info/v3/details");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("HubSpot account-info call failed: {Status}", (int)response.StatusCode);
            throw new InvalidOperationException("HubSpot account-info call failed.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var portalId = root.TryGetProperty("portalId", out var p) && p.TryGetInt64(out var pid) ? pid : 0L;
        return new HubSpotAccountInfo(
            PortalId: portalId,
            AccountType: GetString(root, "accountType"),
            TimeZone: GetString(root, "timeZone"),
            CompanyCurrency: GetString(root, "companyCurrency"),
            UiDomain: GetString(root, "uiDomain"));
    }

    private static HubSpotTokenResult ParseTokenResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var accessToken = GetString(root, "access_token")
            ?? throw new InvalidOperationException("HubSpot token response missing access_token.");
        var refreshToken = GetString(root, "refresh_token")
            ?? throw new InvalidOperationException("HubSpot token response missing refresh_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
            ? seconds
            : 1800; // HubSpot default ~30 min; only a fallback — we always read expires_in when present.
        return new HubSpotTokenResult(accessToken, refreshToken, expiresIn);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ExtractErrorCode(string body)
    {
        // Surface only HubSpot's error code/status for logs — never the body verbatim (defensive).
        try
        {
            using var doc = JsonDocument.Parse(body);
            return GetString(doc.RootElement, "status")
                ?? GetString(doc.RootElement, "error")
                ?? "unknown_error";
        }
        catch { return "unparseable_error"; }
    }
}
