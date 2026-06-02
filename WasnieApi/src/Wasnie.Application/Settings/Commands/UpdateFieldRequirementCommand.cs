using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Settings.Commands;

public sealed record UpdateFieldRequirementCommand(
    string EntityName,
    string FieldName,
    bool IsRequired) : IRequest<Result<Unit>>;

public sealed class UpdateFieldRequirementHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuditService auditService,
    IAuthorizationService authorizationService)
    : IRequestHandler<UpdateFieldRequirementCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        UpdateFieldRequirementCommand request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.SettingsUpdate, cancellationToken);

        var setting = await db.FieldRequirementSettings
            .FirstOrDefaultAsync(
                s => s.EntityName == request.EntityName && s.FieldName == request.FieldName,
                cancellationToken);

        if (setting is null)
            return Result<Unit>.Failure($"Field '{request.EntityName}.{request.FieldName}' is not in the configurable catalog.");

        var before = new { setting.EntityName, setting.FieldName, setting.IsRequired };
        setting.SetRequired(request.IsRequired);
        await db.SaveChangesAsync(cancellationToken);
        var after = new { setting.EntityName, setting.FieldName, setting.IsRequired };

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.FieldRequirementChanged,
                ResourceType: ResourceTypes.FieldRequirement,
                ResourceId: $"{request.EntityName}.{request.FieldName}",
                ActorUserId: currentUser.UserId ?? "system",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: $"{request.EntityName}.{request.FieldName}",
                Before: EntityDiff.Serialize(before),
                After: EntityDiff.Serialize(after)), cancellationToken);
        }
        catch { /* audit failures must not block the user operation (Rule 5.3.3) */ }

        return Result<Unit>.Success(Unit.Value);
    }
}
