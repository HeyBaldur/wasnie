using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Plans;

public sealed class CreatePlanHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IRequestHandler<CreatePlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(CreatePlanCommand request, CancellationToken cancellationToken)
    {
        var period = DateRange.Of(request.EffectiveStart, request.EffectiveEnd);

        var plan = Plan.Create(
            tenantContext.TenantId,
            request.Name,
            request.Description,
            period,
            request.Currency,
            currentUser.UserId ?? "system");

        db.CompensationPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);

        return Result<PlanDto>.Success(CompensationMapper.ToPlanDto(plan));
    }
}
