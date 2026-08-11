using System.Net;
using MediatR;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Integrations.HubSpot;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>Starts the OAuth handshake: mints an anti-CSRF state and returns the HubSpot authorization URL.</summary>
public sealed record StartHubSpotConnectionCommand : IRequest<Result<HubSpotConnectResultDto>>;

public sealed class StartHubSpotConnectionHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuthorizationService authorizationService,
    IPaidPlanGate paidPlanGate,
    IClock clock,
    IGuidGenerator guid,
    IOptions<HubSpotOptions> options)
    : IRequestHandler<StartHubSpotConnectionCommand, Result<HubSpotConnectResultDto>>
{
    private readonly HubSpotOptions _opts = options.Value;

    public async Task<Result<HubSpotConnectResultDto>> Handle(
        StartHubSpotConnectionCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);
        // Metered capability: the plan is checked after the permission, so an admin on Free is
        // told the truth ("not in your plan") instead of a bare Forbidden. Frozen, not deleted —
        // a downgraded tenant keeps its stored connection and resumes on upgrade.
        await paidPlanGate.RequirePaidPlanAsync("The HubSpot integration", cancellationToken);

        if (!_opts.IsConfigured)
            return Result<HubSpotConnectResultDto>.Failure(
                "HubSpot is not configured on the server. Set HubSpot:ClientId, ClientSecret and TokenEncryptionKey.");

        var now = clock.UtcNowOffset;
        var state = HubSpotOAuthState.Create(
            guid.NewGuid(),
            tenantContext.TenantId,
            currentUser.UserId ?? "system",
            now,
            TimeSpan.FromMinutes(_opts.StateTtlMinutes));

        db.HubSpotOAuthStates.Add(state);
        await db.SaveChangesAsync(cancellationToken);

        var url = BuildAuthorizationUrl(state.Id);
        return Result<HubSpotConnectResultDto>.Success(new HubSpotConnectResultDto(url));
    }

    private string BuildAuthorizationUrl(Guid state)
    {
        // HubSpot expects: client_id, redirect_uri, scope (space-separated), state.
        var query =
            $"client_id={WebUtility.UrlEncode(_opts.ClientId)}" +
            $"&redirect_uri={WebUtility.UrlEncode(_opts.RedirectUri)}" +
            $"&scope={WebUtility.UrlEncode(_opts.Scopes)}" +
            $"&state={WebUtility.UrlEncode(state.ToString())}";
        return $"{_opts.AuthorizationEndpoint}?{query}";
    }
}
