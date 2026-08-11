using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// Verification-only "ping": makes ONE trivial authenticated call to HubSpot (account info) using the
/// stored token, to confirm the OAuth connection works. This is NOT Phase 2 — it maps nothing and
/// creates nothing. Returns safe account info; never the token.
/// </summary>
public sealed record PingHubSpotQuery : IRequest<Result<HubSpotPingResultDto>>;

public sealed class PingHubSpotHandler(
    ITenantContext tenantContext,
    IAuthorizationService authorizationService,
    IPaidPlanGate paidPlanGate,
    IHubSpotTokenProvider tokenProvider,
    IHubSpotOAuthClient client)
    : IRequestHandler<PingHubSpotQuery, Result<HubSpotPingResultDto>>
{
    public async Task<Result<HubSpotPingResultDto>> Handle(PingHubSpotQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);
        // Metered capability: the plan is checked after the permission, so an admin on Free is
        // told the truth ("not in your plan") instead of a bare Forbidden. Frozen, not deleted —
        // a downgraded tenant keeps its stored connection and resumes on upgrade.
        await paidPlanGate.RequirePaidPlanAsync("The HubSpot integration", cancellationToken);

        var accessToken = await tokenProvider.GetValidAccessTokenAsync(tenantContext.TenantId, cancellationToken);
        if (accessToken is null)
            return Result<HubSpotPingResultDto>.Failure(
                "HubSpot is not connected, or the connection needs to be re-authorized.");

        try
        {
            var info = await client.GetAccountInfoAsync(accessToken, cancellationToken);
            return Result<HubSpotPingResultDto>.Success(new HubSpotPingResultDto(
                info.PortalId, info.AccountType, info.TimeZone, info.CompanyCurrency, info.UiDomain));
        }
        catch
        {
            return Result<HubSpotPingResultDto>.Failure("The HubSpot test call failed.");
        }
    }
}
