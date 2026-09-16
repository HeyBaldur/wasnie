using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Settings;

namespace Wasnie.Application.Features.Favorites;

// ── The one seam every section plugs into ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Everything the favorites feature needs to know about ONE kind of entity (KAN-64).
///
/// ★★ THIS IS THE WHOLE EXTENSION POINT. The table, the endpoints, the handlers, the limit and the client store are
/// shared; a section only answers two questions about its own entity. Favorites for transactions is a new
/// <see cref="FavoriteEntityType"/> value and a class implementing this — no handler, controller or store changes.
///
/// ★ VISIBILITY IS THE PROVIDER'S, because it differs per type and is not the favorite's to decide: a payee is visible
/// through <c>IPayeeAccessGuard</c> (row by row), a plan through a permission. Asking the provider on BOTH paths — the
/// write and every read — is what keeps a favorite from outliving the access that allowed it.
/// </summary>
public interface IFavoriteEntityProvider
{
    FavoriteEntityType EntityType { get; }

    /// <summary>
    /// Whether the caller may favorite this entity: it exists in the tenant and the caller may see it. False for "does
    /// not exist" and "exists but not yours" alike, so the answer cannot be used to probe for ids. A missing
    /// PERMISSION may throw (403) the way the section's own endpoints do.
    /// </summary>
    Task<bool> CanFavoriteAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// The rows to paint for these ids — only the ones that still exist AND the caller may still see. Never throws for a
    /// missing permission: it returns nothing, since a list the caller cannot see is simply empty.
    /// </summary>
    Task<IReadOnlyList<FavoriteItemDto>> ResolveAsync(IReadOnlyCollection<Guid> entityIds, CancellationToken cancellationToken);
}

/// <summary>
/// One row of the quick-access table. Generic across types; a field a type does not have is null.
/// </summary>
/// <param name="Code">Payee: the employee code. Plan: null (plans have no code).</param>
/// <param name="Version">Plan: the version number. Payee: null.</param>
/// <param name="Status">The entity's status enum NAME (e.g. <c>Active</c>, <c>Draft</c>) — a code the client
/// translates, never prose (§C1).</param>
public sealed record FavoriteItemDto(Guid EntityId, string Name, string? Code, int? Version, string Status);

/// <summary>The refusal codes this feature sends. Keys the client translates (§C1); nothing else leaves as prose.</summary>
public static class FavoriteErrors
{
    /// <summary>Unknown type, missing entity, or an entity the caller may not see — one answer for all three.</summary>
    public const string NotFound = "FAVORITES.NOT_FOUND";

    public const string LimitReached = "FAVORITES.LIMIT_REACHED";

    public const string NoSignedInUser = "FAVORITES.NO_USER";
}

/// <summary>Finds the provider for a type. A type without one is treated as unknown, never as "allowed".</summary>
public sealed class FavoriteProviders(IEnumerable<IFavoriteEntityProvider> providers)
{
    private readonly IReadOnlyDictionary<FavoriteEntityType, IFavoriteEntityProvider> _byType =
        providers.ToDictionary(p => p.EntityType);

    public IFavoriteEntityProvider? For(FavoriteEntityType type) => _byType.GetValueOrDefault(type);
}

// ── Requests ───────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>The caller's favorites of one type, resolved for display, by name.</summary>
public sealed record ListFavoritesQuery(FavoriteEntityType EntityType) : IRequest<Result<IReadOnlyList<FavoriteItemDto>>>;

/// <summary>Stars an entity for the caller. Idempotent.</summary>
/// <remarks>Deliberately NOT an auditable command: a favorite is a UI preference, not a business event.</remarks>
public sealed record AddFavoriteCommand(FavoriteEntityType EntityType, Guid EntityId) : IRequest<Result>;

/// <summary>Un-stars an entity for the caller. Idempotent.</summary>
public sealed record RemoveFavoriteCommand(FavoriteEntityType EntityType, Guid EntityId) : IRequest<Result>;

// ── Handlers ───────────────────────────────────────────────────────────────────────────────────────────────────────

internal static class MyFavorites
{
    /// <summary>
    /// The caller's rows. The tenant query filter is only the floor — it cannot see the principal — so the USER half is
    /// added here, on every read, the same way the assistant and the UI preferences do it.
    /// </summary>
    public static IQueryable<Favorite> Of(IApplicationDbContext db, ITenantContext tenant, string userId, FavoriteEntityType type) =>
        db.Favorites.Where(f => f.TenantId == tenant.TenantId && f.UserId == userId && f.EntityType == type);
}

public sealed class ListFavoritesHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    FavoriteProviders providers)
    : IRequestHandler<ListFavoritesQuery, Result<IReadOnlyList<FavoriteItemDto>>>
{
    public async Task<Result<IReadOnlyList<FavoriteItemDto>>> Handle(ListFavoritesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId) || !tenantContext.IsResolved)
            return Result<IReadOnlyList<FavoriteItemDto>>.Failure(FavoriteErrors.NoSignedInUser);

        var provider = providers.For(request.EntityType);
        if (provider is null)
            return Result<IReadOnlyList<FavoriteItemDto>>.Failure(FavoriteErrors.NotFound);

        var ids = await MyFavorites.Of(db, tenantContext, userId, request.EntityType)
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return Result<IReadOnlyList<FavoriteItemDto>>.Success([]);

        // ★ RESOLVED THROUGH THE PROVIDER, NEVER RETURNED AS BARE IDS. A favorite on an entity that was deleted, or that
        // the caller can no longer see, must not render — and an id list would leave that decision to the client.
        var items = await provider.ResolveAsync(ids, cancellationToken);

        return Result<IReadOnlyList<FavoriteItemDto>>.Success(
            items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }
}

public sealed class AddFavoriteHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    FavoriteProviders providers,
    IClock clock,
    IGuidGenerator guids)
    : IRequestHandler<AddFavoriteCommand, Result>
{
    public async Task<Result> Handle(AddFavoriteCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId) || !tenantContext.IsResolved)
            return Result.Failure(FavoriteErrors.NoSignedInUser);

        var provider = providers.For(request.EntityType);
        if (provider is null)
            return Result.Failure(FavoriteErrors.NotFound);

        // ★ VISIBILITY FIRST, before anything is read about the caller's own rows. Writing a row keyed to an id the
        // caller may not see would let "did the star stick?" answer "does that id exist?" (same reasoning as the
        // assistant pins).
        if (!await provider.CanFavoriteAsync(request.EntityId, cancellationToken))
            return Result.Failure(FavoriteErrors.NotFound);

        var mine = MyFavorites.Of(db, tenantContext, userId, request.EntityType);

        // Already starred is success: the same entity can be starred from the favorites table and from the list.
        // Returning here also skips the limit, so re-starring at exactly the limit never starts failing.
        if (await mine.AnyAsync(f => f.EntityId == request.EntityId, cancellationToken))
            return Result.Success();

        // ★★ THE LIMIT COUNTS WHAT THE USER CAN SEE, not the rows. A favorite whose plan was deleted, or whose payee the
        // user can no longer see, stays in the table (no foreign key) but renders nowhere — counting it would refuse a
        // new star against a list that visibly has room, with no way for the user to find what is taking the space.
        var existingIds = await mine.Select(f => f.EntityId).ToListAsync(cancellationToken);
        if (existingIds.Count >= Favorite.MaxPerType)
        {
            var visible = await provider.ResolveAsync(existingIds, cancellationToken);
            if (visible.Count >= Favorite.MaxPerType)
                return Result.Failure(FavoriteErrors.LimitReached);
        }

        db.Favorites.Add(Favorite.Create(
            guids.NewGuid(), tenantContext.TenantId, userId, request.EntityType, request.EntityId, clock.UtcNowOffset));

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class RemoveFavoriteHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IRequestHandler<RemoveFavoriteCommand, Result>
{
    public async Task<Result> Handle(RemoveFavoriteCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId) || !tenantContext.IsResolved)
            return Result.Failure(FavoriteErrors.NoSignedInUser);

        // ★ NO VISIBILITY CHECK, deliberately (§D4). It only ever deletes the caller's OWN row, so it reveals nothing
        // about the entity — and a user who lost access to something must still be able to clear the star from it.
        var row = await MyFavorites.Of(db, tenantContext, userId, request.EntityType)
            .FirstOrDefaultAsync(f => f.EntityId == request.EntityId, cancellationToken);

        if (row is null)
            return Result.Success();

        db.Favorites.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
