using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Settings;

/// <summary>
/// What kind of thing a <see cref="Favorite"/> points at (KAN-64).
///
/// ★ ADDING A SECTION IS ADDING A VALUE HERE AND ONE PROVIDER — nothing else. The table, the endpoints, the
/// handlers and the client store are shared by every type; a test fails if a value has no provider.
///
/// Stored as its NAME, not its number: a reordered enum must not silently re-point every favorite.
/// </summary>
public enum FavoriteEntityType
{
    Payee,
    Plan,
}

/// <summary>
/// One user's "star" on one entity (KAN-64).
///
/// ★ A PREFERENCE, NOT A FACT. Removing a favorite deletes the row: it records nothing that happened in the
/// business, so there is no history to keep (§B6 is about facts). That is also why the toggle is not audited.
///
/// ★ OWNERSHIP IS (TenantId, UserId), like <see cref="UserUiPreference"/>: every read matches both, so two users of
/// one tenant never see each other's favorites.
///
/// ★ NO FOREIGN KEY TO THE ENTITY, on purpose: one table serves every type, so <see cref="EntityId"/> cannot point at
/// one table. A favorite whose entity is gone (a deleted Draft plan) simply stops resolving — the providers read the
/// entity, never the favorite alone, so it neither renders nor counts toward the limit.
/// </summary>
public sealed class Favorite : Entity
{
    /// <summary>Per user and type. The quick-access table has no paging, so the list must stay short.</summary>
    public const int MaxPerType = 50;

    public Guid TenantId { get; private set; }

    /// <summary>ASP.NET Identity user id (450, same width as the other user-owned tables).</summary>
    public string UserId { get; private set; } = string.Empty;

    public FavoriteEntityType EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Favorite() { }

    public static Favorite Create(
        Guid id, Guid tenantId, string userId, FavoriteEntityType entityType, Guid entityId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("UserId must not be empty.");
        if (!Enum.IsDefined(entityType))
            throw new DomainException("Unknown favorite entity type.");
        if (entityId == Guid.Empty)
            throw new DomainException("EntityId must not be empty.");

        return new Favorite
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = now,
        };
    }
}
