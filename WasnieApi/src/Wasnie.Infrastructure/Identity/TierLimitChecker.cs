using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;

namespace Wasnie.Infrastructure.Identity;

public sealed class TierLimitChecker(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuditService auditService)
    : ITierLimitChecker
{
    public async Task EnsurePayeeLimitAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
        {
            return;
        }

        var tier = tenant.Tier;
        var limits = TierLimits.Limits[tier];

        if (limits.MaxPayees == int.MaxValue)
        {
            return;
        }

        var count = await db.Payees.CountAsync(cancellationToken);

        if (count >= limits.MaxPayees)
        {
            await LogTierLimitDenialAsync("payees", count, limits.MaxPayees, tier, cancellationToken);
            var upgradeTier = GetUpgradeTier(tier);
            throw new TierLimitExceededException(tier.ToString(), "payees", count, limits.MaxPayees, upgradeTier);
        }
    }

    public async Task EnsurePlanLimitAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
        {
            return;
        }

        var tier = tenant.Tier;
        var limits = TierLimits.Limits[tier];

        if (limits.MaxPlans == int.MaxValue)
        {
            return;
        }

        var count = await db.CompensationPlans.CountAsync(cancellationToken);

        if (count >= limits.MaxPlans)
        {
            await LogTierLimitDenialAsync("plans", count, limits.MaxPlans, tier, cancellationToken);
            var upgradeTier = GetUpgradeTier(tier);
            throw new TierLimitExceededException(tier.ToString(), "plans", count, limits.MaxPlans, upgradeTier);
        }
    }

    private async Task LogTierLimitDenialAsync(
        string resourceType, int count, int limit, Tier tier, CancellationToken cancellationToken)
    {
        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.TierLimitExceeded,
                ResourceType: ResourceTypes.Auth,
                ResourceId: currentUser.UserId ?? "anonymous",
                ActorUserId: currentUser.UserId ?? "anonymous",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: $"{resourceType}:{tier}:{count}/{limit}"), cancellationToken);
        }
        catch { /* audit failures must not block */ }
    }

    private static string? GetUpgradeTier(Tier current) => current switch
    {
        Tier.Free => Tier.Starter.ToString(),
        Tier.Starter => Tier.Growth.ToString(),
        Tier.Growth => Tier.Scale.ToString(),
        Tier.Scale => Tier.Enterprise.ToString(),
        _ => null,
    };
}
