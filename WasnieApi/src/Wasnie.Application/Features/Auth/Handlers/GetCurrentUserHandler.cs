using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Queries;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class GetCurrentUserHandler(
    ICurrentUserService currentUser,
    IClaimsService claimsService,
    ITenantContext tenantContext,
    IIdentityService identityService,
    IApplicationDbContext db)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        var userId = currentUser.UserId ?? string.Empty;
        var role = claimsService.GetRole() ?? string.Empty;
        var permissions = RolePermissions.GetPermissions(role).ToList();
        var tier = tenant?.Tier.ToString() ?? "Free";
        var emailConfirmed = !string.IsNullOrEmpty(userId)
            && await identityService.IsEmailConfirmedAsync(userId);

        return new CurrentUserDto(
            UserId: userId,
            Email: currentUser.Email ?? string.Empty,
            Role: role,
            TenantId: tenantContext.TenantId,
            TenantSlug: tenant?.Slug ?? string.Empty,
            Tier: tier,
            HasSelectedPlan: tenant?.HasSelectedPlan ?? false,
            EmailConfirmed: emailConfirmed,
            IsQualified: tenant?.IsQualified ?? false,
            Permissions: permissions);
    }
}
