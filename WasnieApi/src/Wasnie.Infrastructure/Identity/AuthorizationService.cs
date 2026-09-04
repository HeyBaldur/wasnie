using Wasnie.Application.Authorization;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;

namespace Wasnie.Infrastructure.Identity;

public sealed class AuthorizationService(
    IClaimsService claimsService,
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IAuditService auditService)
    : Wasnie.Application.Common.Interfaces.IAuthorizationService
{
    /// <summary>
    /// ★ THE SAME ROLE LOOKUP RequireAsync USES, without the throw or the audit entry. Reading the
    /// role twice through two different paths is how the two would eventually disagree.
    /// </summary>
    public Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default)
    {
        var role = claimsService.GetRole();
        return Task.FromResult(role is not null && RolePermissions.HasPermission(role, permission));
    }

    public async Task RequireAsync(string permission, CancellationToken cancellationToken = default)
    {
        var role = claimsService.GetRole();

        if (role is not null && RolePermissions.HasPermission(role, permission))
            return;

        // Rule 5.1.4: denied authorization decisions MUST be logged
        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.PermissionDenied,
                ResourceType: ResourceTypes.Auth,
                ResourceId: currentUser.UserId ?? "anonymous",
                ActorUserId: currentUser.UserId ?? "anonymous",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: permission), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            throw; // Missing/invalid tenant claim — propagate so middleware returns 401.
        }
        catch { /* audit failures must not block (Rule 5.3.3) */ }

        throw new ForbiddenException(permission);
    }
}
