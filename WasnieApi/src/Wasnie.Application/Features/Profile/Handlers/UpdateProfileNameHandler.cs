using MediatR;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Profile.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Handlers;

public sealed class UpdateProfileNameHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IAuditService auditService,
    ITenantContext tenantContext)
    : IRequestHandler<UpdateProfileNameCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateProfileNameCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result<bool>.Failure("Not authenticated.");

        var oldFirst = await identityService.GetClaimAsync(userId, "given_name") ?? string.Empty;
        var oldLast = await identityService.GetClaimAsync(userId, "family_name") ?? string.Empty;

        var firstOk = await identityService.UpdateClaimAsync(userId, "given_name", request.FirstName.Trim());
        var lastOk = await identityService.UpdateClaimAsync(userId, "family_name", request.LastName.Trim());

        if (!firstOk || !lastOk)
            return Result<bool>.Failure("Failed to update name. Please try again.");

        var email = currentUser.Email ?? string.Empty;

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.ProfileNameUpdated,
                ResourceType: ResourceTypes.Auth,
                ResourceId: userId,
                ActorUserId: userId,
                ActorEmail: email,
                DisplayName: email,
                Before: new { FirstName = oldFirst, LastName = oldLast },
                After: new { FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim() }),
                cancellationToken);
        }
        catch { /* audit must not block */ }

        return Result<bool>.Success(true);
    }
}
