using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Profile.DTOs;
using Wasnie.Application.Features.Profile.Queries;

namespace Wasnie.Application.Features.Profile.Handlers;

public sealed class GetProfileHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IApplicationDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock)
    : IRequestHandler<GetProfileQuery, ProfileDto>
{
    public async Task<ProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? string.Empty;
        var now = clock.UtcNowOffset;

        var firstName = await identityService.GetClaimAsync(userId, "given_name") ?? string.Empty;
        var lastName = await identityService.GetClaimAsync(userId, "family_name") ?? string.Empty;
        var email = currentUser.Email ?? string.Empty;

        var hasPending = await dbContext.EmailChangeTokens
            .AnyAsync(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now, cancellationToken);

        var tenant = await dbContext.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        return new ProfileDto(
            FirstName: firstName,
            LastName: lastName,
            Email: email,
            HasPendingEmailChange: hasPending,
            CompanyName: tenant?.Name ?? string.Empty,
            OrganizationSlug: tenant?.Slug ?? string.Empty);
    }
}
