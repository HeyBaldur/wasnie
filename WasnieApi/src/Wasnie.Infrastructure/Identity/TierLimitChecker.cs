using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Identity;

namespace Wasnie.Infrastructure.Identity;

/// <summary>
/// Enforces the payee / compensation-plan caps of the tenant's plan.
///
/// ★ KAN-77: THE LIMITS COME FROM THE PLAN CATALOG, NOT A HARD-CODED TIER TABLE. A paying tenant is held to its
/// own plan (<c>Tenant.PlanCode</c>); a trial experiences the default plan — the product it is trying out, in
/// full. A null limit means unlimited, which is what the single €299 plan has today, so these checks are
/// dormant until a plan with a cap is configured. The interface and the refusal shape are unchanged: callers
/// and the client's limit modal keep working as they did.
/// </summary>
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
    ISandboxScope sandboxScope,
    ISubscriptionPlanCatalog catalog,
    IClock clock)
    : ITierLimitChecker
{
    /// <summary>
    /// Seats in use and seats allowed (KAN-32). Both halves of "used" are COUNTED, never stored:
    /// active access rows plus outstanding invitations. See <see cref="SeatUsage"/> for why the
    /// outstanding half counts.
    /// </summary>
    public async Task<SeatUsage> GetSeatUsageAsync(CancellationToken cancellationToken = default)
    {
        var plan = await CurrentPlanAsync(cancellationToken);
        var now = clock.UtcNowOffset;

        var activeUsers = await db.TenantUsers
            .Where(TenantUser.ActiveSpec)
            .CountAsync(cancellationToken);

        var pending = await db.Invitations
            .Where(Invitation.PendingSpec(now))
            .CountAsync(cancellationToken);

        return new SeatUsage(activeUsers, pending, plan?.MaxUsers, plan?.Code ?? string.Empty);
    }

    public async Task EnsureSeatAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (sandboxScope.IsSandbox) return;

        var usage = await GetSeatUsageAsync(cancellationToken);
        if (usage.HasRoom) return;

        var limit = usage.Limit!.Value;
        await LogLimitDenialAsync("users", usage.Used, limit, usage.Tier, cancellationToken);
        throw new TierLimitExceededException(usage.Tier, "users", usage.Used, limit, null);
    }

    public async Task EnsurePayeeLimitAsync(CancellationToken cancellationToken = default)
    {
        if (sandboxScope.IsSandbox) return;

        var plan = await CurrentPlanAsync(cancellationToken);
        if (plan?.MaxPayees is not int maxPayees)
            return;

        var count = await db.Payees.CountAsync(cancellationToken);
        if (count >= maxPayees)
        {
            await LogLimitDenialAsync("payees", count, maxPayees, plan.Code, cancellationToken);
            throw new TierLimitExceededException(plan.Code, "payees", count, maxPayees, UpgradeTarget(plan));
        }
    }

    public async Task EnsurePlanLimitAsync(CancellationToken cancellationToken = default)
    {
        if (sandboxScope.IsSandbox) return;

        var plan = await CurrentPlanAsync(cancellationToken);
        if (plan?.MaxPlans is not int maxPlans)
            return;

        var count = await db.CompensationPlans.CountAsync(cancellationToken);
        if (count >= maxPlans)
        {
            await LogLimitDenialAsync("plans", count, maxPlans, plan.Code, cancellationToken);
            throw new TierLimitExceededException(plan.Code, "plans", count, maxPlans, UpgradeTarget(plan));
        }
    }

    public async Task<PayeeImportLimitCheck> CheckPayeeImportLimitAsync(int incomingCount, CancellationToken cancellationToken = default)
    {
        var plan = await CurrentPlanAsync(cancellationToken);
        if (plan is null)
            return new(false, 0, 0, "Unknown");

        if (plan.MaxPayees is not int maxPayees)
            return new(false, 0, 0, plan.Code);

        var current = await db.Payees.CountAsync(cancellationToken);
        if (current + incomingCount > maxPayees)
        {
            await LogLimitDenialAsync("payees_import", current, maxPayees, plan.Code, cancellationToken);
            return new(true, current, maxPayees, plan.Code);
        }

        return new(false, current, maxPayees, plan.Code);
    }

    /// <summary>The tenant's own plan when it has subscribed to one we still sell; otherwise the default plan.</summary>
    private async Task<SubscriptionPlanDefinition?> CurrentPlanAsync(CancellationToken cancellationToken)
    {
        var tenantPlanCode = await db.Tenants
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => new { t.PlanCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantPlanCode is null)
            return null;

        return catalog.Find(tenantPlanCode.PlanCode) ?? catalog.Default;
    }

    /// <summary>
    /// A plan with more room than the current one, cheapest first by limits — what the modal offers. Null when
    /// nothing in the catalog is bigger (today: always, with one unlimited plan).
    /// </summary>
    private string? UpgradeTarget(SubscriptionPlanDefinition current) =>
        catalog.All
            .Where(p => !string.Equals(p.Code, current.Code, StringComparison.OrdinalIgnoreCase))
            .Where(p => (p.MaxPayees ?? int.MaxValue) >= (current.MaxPayees ?? int.MaxValue)
                     && (p.MaxPlans ?? int.MaxValue) >= (current.MaxPlans ?? int.MaxValue))
            .OrderBy(p => p.MaxPayees ?? int.MaxValue)
            .Select(p => p.Code)
            .FirstOrDefault();

    private async Task LogLimitDenialAsync(
        string resourceType, int count, int limit, string planCode, CancellationToken cancellationToken)
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
                DisplayName: $"{resourceType}:{planCode}:{count}/{limit}"), cancellationToken);
        }
        catch { /* audit failures must not block */ }
    }
}
