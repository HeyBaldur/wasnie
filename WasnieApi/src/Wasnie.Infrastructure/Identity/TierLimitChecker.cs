using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;

namespace Wasnie.Infrastructure.Identity;

/// <remarks>
/// ★★ LOS LÍMITES DEL PLAN CONTRATADO NO ALCANZAN AL SANDBOX, y hubo que descubrirlo en pantalla: un
/// tenant del plan gratuito no podía ni empezar el recorrido guiado — «Plan Limit Reached: 1/1» —
/// porque el plan de PRÁCTICA contaba contra su cupo de planes reales.
///
/// Los cupos existen para acotar lo que la empresa opera de verdad; los datos del recorrido viven en
/// otro esquema, se borran de un botón y no generan un solo pago. Cobrarle al usuario su cupo por
/// aprender es exactamente al revés de para qué existe el recorrido.
///
/// ★ SE PREGUNTA POR EL ÁMBITO, NO POR LA TABLA. El contador seguiría contando bien aunque mirara el
/// esquema equivocado; lo que decide es si esta petición es de práctica, y eso sólo lo sabe el ámbito.
/// </remarks>
public sealed class TierLimitChecker(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuditService auditService,
    ISandboxScope sandboxScope)
    : ITierLimitChecker
{
    public async Task EnsurePayeeLimitAsync(CancellationToken cancellationToken = default)
    {
        if (sandboxScope.IsSandbox) return;

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
        if (sandboxScope.IsSandbox) return;

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

    public async Task<PayeeImportLimitCheck> CheckPayeeImportLimitAsync(int incomingCount, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return new(false, 0, 0, "Unknown");

        var tier = tenant.Tier;
        var limits = TierLimits.Limits[tier];

        if (limits.MaxPayees == int.MaxValue)
            return new(false, 0, 0, tier.ToString());

        var current = await db.Payees.CountAsync(cancellationToken);

        if (current + incomingCount > limits.MaxPayees)
        {
            await LogTierLimitDenialAsync("payees_import", current, limits.MaxPayees, tier, cancellationToken);
            return new(true, current, limits.MaxPayees, tier.ToString());
        }

        return new(false, current, limits.MaxPayees, tier.ToString());
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
