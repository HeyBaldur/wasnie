using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// The guard that stops a tenant locking itself out (KAN-32).
///
/// ★★ WITHOUT IT, ONE MISCLICK IS PERMANENT. Deactivating or demoting the last administrator leaves
/// nobody who can invite, promote or re-open anything, and no support path reaches inside the product
/// to undo it. It is the one place where refusing a legitimate-looking request is obviously right.
///
/// ★ IT COUNTS ACTIVE ADMINS, NOT ADMINS. A tenant whose only other administrator is deactivated is a
/// tenant with one administrator, whatever the roles table says.
/// </summary>
internal static class LastAdminGuard
{
    public static async Task EnsureNotLastAdminAsync(
        IApplicationDbContext db,
        string userId,
        CancellationToken cancellationToken)
    {
        // KAN-91. ONE QUERY, AND IT ASKS THE MEMBERSHIPS. The first version walked every active user
        // and asked Identity for their roles one at a time: N round trips, and N wrong answers once a
        // person could hold a different role in another workspace. The tenant filter scopes this to the
        // current workspace, which is exactly the population the rule is about.
        var isAdminHere = await db.TenantUsers
            .Where(TenantUser.ActiveSpec)
            .AnyAsync(u => u.UserId == userId && u.Role == Roles.TenantAdmin, cancellationToken);

        if (!isAdminHere) return;

        var otherActiveAdmins = await db.TenantUsers
            .Where(TenantUser.ActiveSpec)
            .CountAsync(u => u.UserId != userId && u.Role == Roles.TenantAdmin, cancellationToken);

        if (otherActiveAdmins == 0)
            throw new DomainCodedException(InvitationRefusal.LastAdmin);
    }
}

/// <summary>
/// Closes somebody's access (KAN-32). Nothing is deleted and nothing is nulled — see
/// <c>TenantUser</c> and §B6.
/// </summary>
public sealed class DeactivateUserHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<DeactivateUserCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        // ★ NOT YOURSELF. An admin who switches their own access off is locked out immediately, with
        // the button that would undo it on the other side of the door.
        if (string.Equals(request.UserId, currentUser.UserId, StringComparison.Ordinal))
            throw new DomainCodedException(InvitationRefusal.CannotActOnSelf);

        await LastAdminGuard.EnsureNotLastAdminAsync(db, request.UserId, cancellationToken);

        var access = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.UserId == request.UserId, cancellationToken);

        if (access is null)
            return Result<bool>.Failure("User not found in this tenant.");

        access.Deactivate(currentUser.UserId ?? string.Empty, clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.UserDeactivated,
            ResourceType: "User",
            ResourceId: request.UserId,
            ActorUserId: currentUser.UserId ?? string.Empty,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: await identityService.FindEmailByUserIdAsync(request.UserId) ?? request.UserId),
            cancellationToken);

        return Result<bool>.Success(true);
    }
}

/// <summary>Re-opens access, WITHOUT erasing the record that it was ever closed (§B6).</summary>
public sealed class ReactivateUserHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    ITierLimitChecker tierLimitChecker,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<ReactivateUserCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ReactivateUserCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var access = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.UserId == request.UserId, cancellationToken);

        if (access is null)
            return Result<bool>.Failure("User not found in this tenant.");

        if (access.IsActive)
            return Result<bool>.Success(true);

        // ★ RE-OPENING TAKES A SEAT BACK, so it is checked like an invitation. A deactivated user
        // released one; there is no promise it is still free when somebody comes back.
        await tierLimitChecker.EnsureSeatAvailableAsync(cancellationToken);

        access.Reactivate(currentUser.UserId ?? string.Empty, clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.UserReactivated,
            ResourceType: "User",
            ResourceId: request.UserId,
            ActorUserId: currentUser.UserId ?? string.Empty,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: await identityService.FindEmailByUserIdAsync(request.UserId) ?? request.UserId),
            cancellationToken);

        return Result<bool>.Success(true);
    }
}

/// <summary>
/// Ends somebody's membership of this workspace (KAN-91).
///
/// THE MEMBERSHIP ROW IS DELETED AND NOTHING ELSE IS. The account, its credentials, its other
/// workspaces and every audit entry bearing its name are untouched. KAN-32's "never delete" was
/// written about the PERSON — an audit trail that points at a deleted user is evidence nobody can
/// read — and that promise is kept here: what goes is a grant, not a human being.
///
/// THE AUDIT ENTRY IS WRITTEN BEFORE THE DELETE, on purpose. Afterwards there is no row left to
/// describe, and an entry that cannot say which role the person held is an entry nobody can act on.
/// </summary>
public sealed class RemoveUserHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IAuditService auditService)
    : IRequestHandler<RemoveUserCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RemoveUserCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        // Removing yourself locks you out of the screen that would undo it, exactly as deactivating
        // yourself does.
        if (string.Equals(request.UserId, currentUser.UserId, StringComparison.Ordinal))
            throw new DomainCodedException(InvitationRefusal.CannotActOnSelf);

        // And the last administrator cannot leave: removal ends their authority as completely as
        // demotion does, so it goes through the same guard.
        await LastAdminGuard.EnsureNotLastAdminAsync(db, request.UserId, cancellationToken);

        var access = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.UserId == request.UserId, cancellationToken);

        if (access is null)
            return Result<bool>.Failure("User not found in this tenant.");

        var email = await identityService.FindEmailByUserIdAsync(request.UserId) ?? request.UserId;

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.UserRemoved,
            ResourceType: "User",
            ResourceId: request.UserId,
            ActorUserId: currentUser.UserId ?? string.Empty,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: email,
            Before: new { access.Role, access.IsActive }),
            cancellationToken);

        db.TenantUsers.Remove(access);
        await db.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}

/// <summary>
/// Moves somebody to a different role (KAN-32).
///
/// ★ THE BEFORE AND AFTER BOTH GO IN THE AUDIT ENTRY. "Role changed" without the old value cannot
/// answer the only question anybody asks of it afterwards — what did this person used to be allowed
/// to do.
/// </summary>
public sealed class ChangeUserRoleHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IAuditService auditService)
    : IRequestHandler<ChangeUserRoleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var role = Roles.Canonical(request.Role)
            ?? throw new DomainCodedException(InvitationRefusal.RoleUnknown,
                new Dictionary<string, object?> { ["role"] = request.Role });

        if (string.Equals(request.UserId, currentUser.UserId, StringComparison.Ordinal))
            throw new DomainCodedException(InvitationRefusal.CannotActOnSelf);

        var access = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.UserId == request.UserId, cancellationToken);

        if (access is null)
            return Result<bool>.Failure("User not found in this tenant.");

        // KAN-91. THE ROLE IS WRITTEN ON THE MEMBERSHIP, NOT ON THE ACCOUNT. Identity's roles carry no
        // tenant, so writing there would change what this person may do in EVERY workspace they belong
        // to - promoting somebody in one company because a different company promoted them.
        var before = access.Role;

        if (string.Equals(before, role, StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Success(true);

        // Demoting away from TenantAdmin is the same loss of authority as switching the person off.
        if (string.Equals(before, Roles.TenantAdmin, StringComparison.OrdinalIgnoreCase))
            await LastAdminGuard.EnsureNotLastAdminAsync(db, request.UserId, cancellationToken);

        access.ChangeRole(role);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.UserRoleChanged,
            ResourceType: "User",
            ResourceId: request.UserId,
            ActorUserId: currentUser.UserId ?? string.Empty,
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: await identityService.FindEmailByUserIdAsync(request.UserId) ?? request.UserId,
            Before: new { Role = before },
            After: new { Role = role }),
            cancellationToken);

        return Result<bool>.Success(true);
    }
}
