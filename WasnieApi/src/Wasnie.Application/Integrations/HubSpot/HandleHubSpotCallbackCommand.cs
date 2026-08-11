using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Integrations.HubSpot;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// Completes the OAuth handshake. ANONYMOUS flow: the callback is a top-level browser redirect from
/// HubSpot with no JWT, so the tenant/user are recovered from the validated <c>state</c> row (NOT from
/// the request). Exchanges the code for tokens, encrypts them, and persists the connection per tenant.
/// </summary>
public sealed record HandleHubSpotCallbackCommand(string Code, string State) : IRequest<Result<Unit>>;

public sealed class HandleHubSpotCallbackHandler(
    IApplicationDbContext db,
    IHubSpotOAuthClient client,
    ITokenEncryptionService encryption,
    IAuditService auditService,
    IClock clock,
    IGuidGenerator guid,
    IOptions<HubSpotOptions> options,
    ILogger<HandleHubSpotCallbackHandler> logger)
    : IRequestHandler<HandleHubSpotCallbackCommand, Result<Unit>>
{
    private readonly HubSpotOptions _opts = options.Value;

    public async Task<Result<Unit>> Handle(HandleHubSpotCallbackCommand request, CancellationToken cancellationToken)
    {
        if (!_opts.IsConfigured)
            return Result<Unit>.Failure("HubSpot is not configured on the server.");

        if (string.IsNullOrWhiteSpace(request.Code) || !Guid.TryParse(request.State, out var stateId))
            return Result<Unit>.Failure("Invalid OAuth callback parameters.");

        var now = clock.UtcNowOffset;

        // No tenant context here (anonymous) → look the state up by its random id, ignoring the tenant filter.
        var state = await db.HubSpotOAuthStates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == stateId, cancellationToken);

        if (state is null || !state.IsValid(now))
            return Result<Unit>.Failure("The connection request is invalid or has expired. Please try again.");

        // One-time use: consume the state immediately so a replay can't reuse it.
        state.MarkUsed(now);

        // ★ The plan is re-checked HERE, not only at /connect, and it cannot use IPaidPlanGate: this
        // flow is anonymous, so there is no ambient tenant to ask about — the tenant comes from the
        // state row. The window this closes is real: a tenant can start the OAuth flow on a paid plan
        // and land back here after downgrading (or after the subscription lapses mid-handshake). Without
        // this, that returns with live tokens the sync would then refuse to use, leaving credentials
        // stored for a workspace that is not entitled to them.
        var tier = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == state.TenantId)
            .Select(t => (Tier?)t.Tier)
            .FirstOrDefaultAsync(cancellationToken);

        if (tier is null || !TierFeatures.IncludesPaidFeatures(tier.Value))
        {
            logger.LogWarning(
                "HubSpot callback refused for tenant {TenantId}: tier {Tier} does not include the integration.",
                state.TenantId, tier);
            await db.SaveChangesAsync(cancellationToken); // persist state consumption
            return Result<Unit>.Failure("The HubSpot integration is not included in your current plan.");
        }

        HubSpotTokenResult tokens;
        try
        {
            tokens = await client.ExchangeCodeAsync(request.Code, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never log token material; log only the failure shape.
            logger.LogError(ex, "HubSpot code exchange failed for tenant {TenantId}.", state.TenantId);
            await db.SaveChangesAsync(cancellationToken); // persist state consumption
            return Result<Unit>.Failure("Could not complete the HubSpot connection. Please try again.");
        }

        var portalId = await client.GetPortalIdAsync(tokens.AccessToken, cancellationToken);

        var accessEnc = encryption.Encrypt(tokens.AccessToken);
        var refreshEnc = encryption.Encrypt(tokens.RefreshToken);
        var expiresAt = now.AddSeconds(tokens.ExpiresInSeconds);

        // Upsert the per-tenant connection (ignore the tenant filter — we have no ambient tenant).
        var connection = await db.HubSpotConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == state.TenantId, cancellationToken);

        bool isReconnect;
        if (connection is null)
        {
            connection = HubSpotConnection.Create(
                guid.NewGuid(), state.TenantId, portalId, accessEnc, refreshEnc, expiresAt,
                state.InitiatedByUserId, now);
            db.HubSpotConnections.Add(connection);
            isReconnect = false;
        }
        else
        {
            connection.Reconnect(portalId, accessEnc, refreshEnc, expiresAt, state.InitiatedByUserId, now);
            isReconnect = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        await SafeAuditAsync(new AuditEntry(
            TenantId: state.TenantId,
            Action: isReconnect ? AuditActions.HubSpotReconnected : AuditActions.HubSpotConnected,
            ResourceType: ResourceTypes.Integration,
            ResourceId: "HubSpot",
            ActorUserId: state.InitiatedByUserId,
            ActorEmail: string.Empty,
            DisplayName: $"HubSpot portal {portalId}"), cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }

    private async Task SafeAuditAsync(AuditEntry entry, CancellationToken ct)
    {
        try { await auditService.LogAsync(entry, ct); }
        catch { /* audit must not block the connection (Rule 5.3.3) */ }
    }
}
