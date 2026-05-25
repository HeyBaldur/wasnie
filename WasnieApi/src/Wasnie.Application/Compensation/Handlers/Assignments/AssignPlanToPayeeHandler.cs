using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class AssignPlanToPayeeHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IRequestHandler<AssignPlanToPayeeCommand, Result<PlanAssignmentDto>>
{
    public async Task<Result<PlanAssignmentDto>> Handle(
        AssignPlanToPayeeCommand request,
        CancellationToken cancellationToken)
    {
        var payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);

        if (payee is null)
        {
            return Result<PlanAssignmentDto>.Failure("Payee not found.");
        }

        var plan = await db.CompensationPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result<PlanAssignmentDto>.Failure("Plan not found.");
        }

        var payeeSnapshot = PayeeReference.Snapshot(payee.Id, payee.FullName, payee.EmployeeCode);
        var period = DateRange.Of(request.EffectiveStart, request.EffectiveEnd);

        var assignment = PlanAssignment.Create(
            tenantContext.TenantId,
            request.PlanId,
            request.PayeeId,
            payeeSnapshot,
            period,
            currentUser.UserId ?? "system");

        db.PlanAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return Result<PlanAssignmentDto>.Success(CompensationMapper.ToPlanAssignmentDto(assignment));
    }
}
