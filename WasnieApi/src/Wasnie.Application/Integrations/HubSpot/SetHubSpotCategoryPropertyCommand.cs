using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// Sets (or clears) the HubSpot property whose value feeds a transaction's Category (WI-CRM-CATEGORY).
/// The tenant declares which of THEIR properties holds the category — Wasnie never imposes a name.
/// A null/blank value turns the feature off (enrichment falls back to the manual lookup table).
/// </summary>
public sealed record SetHubSpotCategoryPropertyCommand(string? PropertyName) : IRequest<Result<Unit>>;

public sealed class SetHubSpotCategoryPropertyHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuthorizationService authorizationService,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<SetHubSpotCategoryPropertyCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(SetHubSpotCategoryPropertyCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);

        var connection = await db.HubSpotConnections
            .FirstOrDefaultAsync(c => c.TenantId == tenantContext.TenantId, cancellationToken);

        if (connection is null)
            return Result<Unit>.Failure("Connect HubSpot before configuring the category property.");

        connection.SetCategoryPropertyName(request.PropertyName, clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.HubSpotCategoryPropertyChanged,
                ResourceType: ResourceTypes.Integration,
                ResourceId: "HubSpot",
                ActorUserId: currentUser.UserId ?? "system",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: connection.CategoryPropertyName ?? "(cleared)"), cancellationToken);
        }
        catch { /* audit must not block (Rule 5.3.3) */ }

        return Result<Unit>.Success(Unit.Value);
    }
}
