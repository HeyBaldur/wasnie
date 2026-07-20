using Wasnie.Application.Integrations.HubSpot;

namespace Wasnie.Application.Common.Interfaces;

/// <summary>
/// Thin HTTP client over HubSpot's OAuth + account endpoints. Implementations MUST NOT log tokens.
/// </summary>
public interface IHubSpotOAuthClient
{
    /// <summary>Exchanges an authorization <paramref name="code"/> for access + refresh tokens.</summary>
    Task<HubSpotTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for a fresh access + refresh token pair.</summary>
    Task<HubSpotRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Resolves the HubSpot portal/hub id for an access token (via the token-info endpoint).</summary>
    Task<long> GetPortalIdAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Fetches non-secret account details — a trivial authenticated call used to verify the connection.</summary>
    Task<HubSpotAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken cancellationToken = default);
}
