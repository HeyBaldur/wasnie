using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;

namespace Wasnie.Application.Authorization;

/// <summary>
/// Answers "which payees may the asking user read" from the role on the token plus the ownership link
/// on <c>Payee.UserId</c>.
///
/// The rules, and why each one is what it is:
///
///   • <b>TenantAdmin / CompManager → everything.</b> Supervision of the whole tenant is their job:
///     they run pay runs, close orphan accounts and write manual adjustments. Restricting them would
///     not close a hole, it would break the product. The tenant query filter still applies, so
///     "everything" always means "everything in THIS tenant".
///
///   • <b>Manager → their own payee, plus their DIRECT reports.</b> The permission table says a manager
///     needs Ledger.Read "to explain a reduced payment to their rep" (RolePermissions.cs) — so the
///     right scope is their reps, not the company. Direct reports only, NOT the transitive tree: a
///     recursive walk would quietly hand a mid-level manager the whole org beneath them, which is the
///     same over-broad access this class exists to end, arriving by a different door. If skip-level
///     visibility is ever wanted it should be an explicit decision with its own test, not a side
///     effect of a JOIN.
///
///   • <b>Rep → their own payee, and only if it is linked.</b>
///
///   • <b>Anything else → nothing.</b> Unknown role, missing role, no user id on the token: all land on
///     <see cref="PayeeVisibility.None"/>.
///
/// ★ EVERY UNRESOLVED CASE DENIES. There is no branch here that ends in "allow" without having proved
/// ownership first. In particular a Manager whose OWN payee is unlinked resolves to None rather than to
/// "all payees with no manager" — the query is anchored on their payee id, so an unresolvable identity
/// produces an empty set instead of a wildcard. That is the single most important property of this
/// file: read it that way when changing it.
///
/// ★ IT IS NOT THE TENANT GUARD. Every query below runs through <see cref="IApplicationDbContext"/>,
/// whose global filter already scopes Payees to the current tenant. This class narrows WITHIN a tenant
/// and never widens across one.
/// </summary>
public sealed class PayeeAccessGuard(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClaimsService claimsService)
    : IPayeeAccessGuard
{
    /// <summary>Roles that supervise the whole tenant. Matched case-insensitively, like RolePermissions.</summary>
    private static readonly HashSet<string> SupervisoryRoles =
        new(StringComparer.OrdinalIgnoreCase) { nameof(Role.TenantAdmin), nameof(Role.CompManager) };

    /// <summary>
    /// Cached for the lifetime of the request (the guard is registered scoped). Two handlers in one
    /// request — or a handler and an assistant tool — must not disagree about who the user is, and
    /// should not pay for the same two queries twice.
    /// </summary>
    private PayeeVisibility? _cached;

    public async Task<PayeeVisibility> GetVisibilityAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null) return _cached;
        return _cached = await ResolveAsync(cancellationToken);
    }

    public async Task<bool> CanReadAsync(Guid payeeId, CancellationToken cancellationToken = default) =>
        (await GetVisibilityAsync(cancellationToken)).Allows(payeeId);

    private async Task<PayeeVisibility> ResolveAsync(CancellationToken cancellationToken)
    {
        var role = claimsService.GetRole();
        if (string.IsNullOrWhiteSpace(role))
            return PayeeVisibility.None;

        if (SupervisoryRoles.Contains(role))
            return PayeeVisibility.Everything;

        // Below this line every role is scoped by ownership, so an anonymous or malformed principal
        // has nothing to be scoped BY. Deny rather than fall through.
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return PayeeVisibility.None;

        // The payee this user owns. Note the tenant filter is implicit — a user id that exists in
        // another tenant's Payees table simply does not come back.
        var ownPayeeId = await db.Payees
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Not linked → owns nothing → sees nothing. This is the case the whole design turns on: a Rep
        // with no link is not "unrestricted pending setup", they are locked out until an admin links
        // them. Every payee in the tenant is in exactly this state right after the migration.
        if (ownPayeeId is null)
            return PayeeVisibility.None;

        if (string.Equals(role, nameof(Role.Manager), StringComparison.OrdinalIgnoreCase))
        {
            var reportIds = await db.Payees
                .Where(p => p.ManagerId == ownPayeeId.Value)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            var visible = new HashSet<Guid>(reportIds) { ownPayeeId.Value };
            return new PayeeVisibility(false, visible);
        }

        if (string.Equals(role, nameof(Role.Rep), StringComparison.OrdinalIgnoreCase))
            return PayeeVisibility.Of(ownPayeeId.Value);

        // ★ EVERY ROLE IS NAMED, OR IT GETS NOTHING. An earlier draft let an unrecognised role fall
        // through to the Rep branch — "their own payee only" reads harmless, and it is not: it means a
        // role added to RolePermissions next year silently acquires access to payee money because
        // nobody edited this file. A new role's scope is a decision somebody makes on purpose, and
        // until they do, the answer is no.
        return PayeeVisibility.None;
    }
}
