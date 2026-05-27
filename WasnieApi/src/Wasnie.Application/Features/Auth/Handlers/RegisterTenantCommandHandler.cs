using MediatR;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.DTOs;
using Wasnie.Application.Features.Auth.Mappings;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Entities;

namespace Wasnie.Application.Features.Auth.Handlers;

public sealed class RegisterTenantCommandHandler(
    IApplicationDbContext dbContext,
    IIdentityService identityService,
    ITokenService tokenService,
    IClock clock,
    IGuidGenerator guid)
    : IRequestHandler<RegisterTenantCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> Handle(
        RegisterTenantCommand request,
        CancellationToken cancellationToken)
    {
        var slugTaken = dbContext.Tenants
            .Any(t => t.Slug == request.TenantSlug);

        if (slugTaken)
        {
            return Result<AuthResultDto>.Failure("Tenant slug is already taken.");
        }

        var tenant = Tenant.Create(request.TenantName, request.TenantSlug, guid.NewGuid(), clock.UtcNowOffset);
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);

        var (succeeded, userId, errors) = await identityService.CreateUserAsync(
            request.AdminEmail,
            request.AdminPassword,
            ["TenantAdmin"],
            new Dictionary<string, string> { ["tenant_id"] = tenant.Id.ToString() });

        if (!succeeded || userId is null)
        {
            return Result<AuthResultDto>.Failure(string.Join("; ", errors));
        }

        var roles = await identityService.GetUserRolesAsync(userId);
        var tokens = await tokenService.GenerateTokenPairAsync(userId, request.AdminEmail, tenant.Id, roles);

        return Result<AuthResultDto>.Success(
            AuthMapper.ToAuthResultDto(userId, request.AdminEmail, tenant.Id, tenant.Slug, roles, tokens));
    }
}
