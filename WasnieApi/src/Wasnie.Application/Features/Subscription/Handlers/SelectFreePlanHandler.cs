using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Subscription;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class SelectFreePlanHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuditService auditService,
    IClock clock)
    : IRequestHandler<SelectFreePlanCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SelectFreePlanCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return Result<bool>.Failure("Tenant not found.");

        var now = clock.UtcNowOffset;

        // Upsert UserSubscription — idempotent if called more than once
        var existing = await db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);

        if (existing is null)
        {
            var sub = UserSubscription.CreateFree(
                id: Guid.NewGuid(),
                tenantId: tenantContext.TenantId,
                billingEmail: currentUser.Email ?? string.Empty,
                now: now);
            db.UserSubscriptions.Add(sub);
        }

        // Mark onboarding complete on the tenant (cached flag for guard performance)
        tenant.SelectPlan(Tier.Free);

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.PlanSelected,
            ResourceType: ResourceTypes.Auth,
            ResourceId: tenantContext.TenantId.ToString(),
            ActorUserId: currentUser.UserId ?? "unknown",
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: $"Plan selected: {Tier.Free}"),
            cancellationToken);

        return Result<bool>.Success(true);
    }
}
