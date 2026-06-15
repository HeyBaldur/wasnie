using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Onboarding.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Onboarding.Handlers;

public sealed class CompleteQualificationCommandHandler(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<CompleteQualificationCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(CompleteQualificationCommand request, CancellationToken cancellationToken)
    {
        if (!request.LegalAccepted)
            return Result<bool>.Failure("You must accept the legal terms to proceed.");

        var tenant = await dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return Result<bool>.Failure("Tenant not found.");

        if (tenant.IsQualified)
            return Result<bool>.Success(true); // idempotent

        var now = clock.UtcNowOffset;
        tenant.Qualify(
            request.Country,
            request.PhoneNumber,
            request.HowHeardAboutUs,
            request.SalesVolumeRange,
            request.CurrentSystem,
            legalAcceptedAt: now,
            legalAcceptedVersion: "1.0",
            now: now);

        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.TenantQualified,
                ResourceType: ResourceTypes.Auth,
                ResourceId: tenantContext.TenantId.ToString(),
                ActorUserId: currentUser.UserId ?? string.Empty,
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: tenant.Name,
                Metadata: new Dictionary<string, string>
                {
                    ["Country"] = request.Country,
                    ["HowHeardAboutUs"] = request.HowHeardAboutUs,
                    ["SalesVolumeRange"] = request.SalesVolumeRange,
                    ["CurrentSystem"] = request.CurrentSystem,
                    ["LegalVersion"] = "1.0",
                }), cancellationToken);
        }
        catch { /* audit must not block */ }

        return Result<bool>.Success(true);
    }
}
