using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Audit;

namespace Wasnie.Infrastructure.Services.HubSpot;

/// <summary>
/// Resolves a usable HubSpot access token for a tenant, refreshing transparently when it is near expiry.
///
/// On refresh: persists the new token pair and discards the old (HubSpot revokes the old access token on
/// refresh, so only the new one is used). On an irrecoverable refresh failure (BAD_REFRESH_TOKEN / revoked)
/// the connection is moved to NeedsReconnect and null is returned — never retried in a loop.
///
/// Tenant-explicit (takes tenantId, ignores the ambient query filter) so it works from both authenticated
/// requests and future background jobs. Returned tokens are SERVER-SIDE only — never sent to the frontend.
/// </summary>
public sealed class HubSpotTokenProvider(
    IApplicationDbContext db,
    IHubSpotOAuthClient client,
    ITokenEncryptionService encryption,
    IAuditService auditService,
    IClock clock,
    IOptions<HubSpotOptions> options)
    : IHubSpotTokenProvider
{
    private readonly HubSpotOptions _opts = options.Value;

    public async Task<string?> GetValidAccessTokenAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await db.HubSpotConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (connection is null
            || connection.Status == Domain.Integrations.HubSpot.HubSpotConnectionStatus.Disconnected
            || connection.Status == Domain.Integrations.HubSpot.HubSpotConnectionStatus.NeedsReconnect)
            return null;

        var now = clock.UtcNowOffset;

        // Still valid (with skew)? Use the current access token.
        if (now < connection.TokenExpiresAt.AddSeconds(-_opts.RefreshSkewSeconds))
            return encryption.Decrypt(connection.AccessTokenEncrypted);

        // Near/after expiry → refresh.
        var refreshToken = encryption.Decrypt(connection.RefreshTokenEncrypted);
        var result = await client.RefreshAsync(refreshToken, cancellationToken);

        if (result.Success && result.Tokens is { } tokens)
        {
            connection.ApplyRefreshedTokens(
                encryption.Encrypt(tokens.AccessToken),
                encryption.Encrypt(tokens.RefreshToken),
                now.AddSeconds(tokens.ExpiresInSeconds),
                now);
            await db.SaveChangesAsync(cancellationToken);

            await SafeAuditAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.HubSpotTokenRefreshed,
                ResourceType: ResourceTypes.Integration,
                ResourceId: "HubSpot",
                ActorUserId: "system",
                ActorEmail: string.Empty,
                DisplayName: "HubSpot token refreshed"), cancellationToken);

            return tokens.AccessToken;
        }

        if (result.IsInvalidRefreshToken)
        {
            // Irrecoverable: mark NeedsReconnect with a clear reason for the UI, and do NOT retry.
            connection.MarkNeedsReconnect(
                "HubSpot rejected the saved refresh token (it may have expired or access was revoked in HubSpot). Please reconnect.",
                now);
            await db.SaveChangesAsync(cancellationToken);

            await SafeAuditAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.HubSpotNeedsReconnect,
                ResourceType: ResourceTypes.Integration,
                ResourceId: "HubSpot",
                ActorUserId: "system",
                ActorEmail: string.Empty,
                DisplayName: result.Error ?? "refresh_failed"), cancellationToken);

            return null;
        }

        // Transient failure (network/5xx): leave the connection alone and report unavailable for now.
        return null;
    }

    private async Task SafeAuditAsync(AuditEntry entry, CancellationToken ct)
    {
        try { await auditService.LogAsync(entry, ct); }
        catch { /* audit must not block (Rule 5.3.3) */ }
    }
}
