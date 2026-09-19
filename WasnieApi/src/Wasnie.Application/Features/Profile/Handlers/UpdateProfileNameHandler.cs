using MediatR;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Profile.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Profile.Handlers;

/// <summary>
/// Renames the signed-in person — unless somebody else maintains that name.
///
/// ★★ THE REFUSAL IS THE SERVER'S, NOT THE SCREEN'S. The profile page hides the name card for an
/// administered account, and hiding is where this kind of thing usually stops; this endpoint carries
/// no permission at all (it is self-service by design, <c>[Authorize]</c> and nothing more), so
/// without the check here a hidden card is a suggestion and <c>PUT /api/profile/name</c> is still
/// open to anybody with a session. The same mistake as hiding a menu entry and leaving the URL
/// reachable, which this product has already made once.
///
/// ★ AND IT REFUSES BEFORE WRITING ANYTHING. The claim update and the audit row both sit after it, so
/// a refused rename leaves no trace of a half-applied one.
/// </summary>
public sealed class UpdateProfileNameHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IApplicationDbContext dbContext,
    IAuditService auditService,
    ITenantContext tenantContext)
    : IRequestHandler<UpdateProfileNameCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateProfileNameCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result<bool>.Failure("Not authenticated.");

        // ★ An administered identity is the administrator's to change, in Users — not this person's.
        if (await AdministeredIdentity.IsAdministeredAsync(dbContext, userId, cancellationToken))
            return Result<bool>.Failure(
                "Your name is maintained by an administrator of this workspace. Ask them to change it.");

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
