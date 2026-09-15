using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Settings;

namespace Wasnie.Application.Features.Favorites;

/// <summary>
/// Payees as favorites.
///
/// ★★ VISIBILITY IS <see cref="IPayeeAccessGuard"/>, NOT JUST <c>Payees.Read</c> (decided for KAN-64). Rep and Manager
/// both hold the permission; the guard is what says a Rep sees only themselves and a Manager their team. The favorites
/// table paints a payee's name, code and status, so it must not show more than the guard allows — even though the
/// payees LIST does not apply the guard today (a known defect outside this ticket).
/// </summary>
public sealed class PayeeFavoriteProvider(
    IApplicationDbContext db,
    IAuthorizationService authorization,
    IPayeeAccessGuard guard) : IFavoriteEntityProvider
{
    public FavoriteEntityType EntityType => FavoriteEntityType.Payee;

    public async Task<bool> CanFavoriteAsync(Guid entityId, CancellationToken cancellationToken)
    {
        await authorization.RequireAsync(Permission.PayeesRead, cancellationToken);

        return await guard.CanReadAsync(entityId, cancellationToken)
            && await db.Payees.AnyAsync(p => p.Id == entityId, cancellationToken);
    }

    public async Task<IReadOnlyList<FavoriteItemDto>> ResolveAsync(
        IReadOnlyCollection<Guid> entityIds, CancellationToken cancellationToken)
    {
        if (entityIds.Count == 0 || !await authorization.HasAsync(Permission.PayeesRead, cancellationToken))
            return [];

        var visibility = await guard.GetVisibilityAsync(cancellationToken);
        var allowed = entityIds.Where(visibility.Allows).ToList();
        if (allowed.Count == 0)
            return [];

        var rows = await db.Payees
            .Where(p => allowed.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode, p.Status })
            .ToListAsync(cancellationToken);

        return rows
            .Select(p => new FavoriteItemDto(p.Id, p.FullName, p.EmployeeCode, null, p.Status.ToString()))
            .ToList();
    }
}

/// <summary>
/// Plans as favorites. Plans have no row-level visibility: <c>Plans.Read</c> sees every plan of the tenant, exactly as
/// the plans list does.
/// </summary>
public sealed class PlanFavoriteProvider(
    IApplicationDbContext db,
    IAuthorizationService authorization) : IFavoriteEntityProvider
{
    public FavoriteEntityType EntityType => FavoriteEntityType.Plan;

    public async Task<bool> CanFavoriteAsync(Guid entityId, CancellationToken cancellationToken)
    {
        await authorization.RequireAsync(Permission.PlansRead, cancellationToken);

        return await db.CompensationPlans.AnyAsync(p => p.Id == entityId, cancellationToken);
    }

    public async Task<IReadOnlyList<FavoriteItemDto>> ResolveAsync(
        IReadOnlyCollection<Guid> entityIds, CancellationToken cancellationToken)
    {
        if (entityIds.Count == 0 || !await authorization.HasAsync(Permission.PlansRead, cancellationToken))
            return [];

        var ids = entityIds.ToList();
        var rows = await db.CompensationPlans
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Version, p.Status })
            .ToListAsync(cancellationToken);

        return rows
            .Select(p => new FavoriteItemDto(p.Id, p.Name, null, p.Version, p.Status.ToString()))
            .ToList();
    }
}
