using MediatR;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class CreatePlanHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuditService auditService,
    IAuthorizationService authorizationService,
    ITierLimitChecker tierLimitChecker)
    : IRequestHandler<CreatePlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(CreatePlanCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansCreate, cancellationToken);
        await tierLimitChecker.EnsurePlanLimitAsync(cancellationToken);
        var period = DateRange.Of(request.EffectiveStart, request.EffectiveEnd);

        var plan = Plan.Create(
            tenantContext.TenantId,
            request.Name,
            request.Description,
            period,
            request.Currency,
            currentUser.UserId ?? "system",
            guid.NewGuid(),
            clock.UtcNowOffset,
            guid.NewGuid());

        db.CompensationPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);

        var dto = CompensationMapper.ToPlanDto(plan, activeAssignmentCount: 0);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.PlanCreated,
                ResourceType: ResourceTypes.Plan,
                ResourceId: plan.Id.ToString(),
                ActorUserId: currentUser.UserId ?? "system",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: plan.Name), cancellationToken);
        }
        catch { /* audit failures must not block the user operation */ }

        return Result<PlanDto>.Success(dto);
    }
}
