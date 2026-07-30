using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Assignments;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class AssignPlanToPayeeHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<AssignPlanToPayeeCommand, Result<PlanAssignmentDto>>
{
    public async Task<Result<PlanAssignmentDto>> Handle(
        AssignPlanToPayeeCommand request,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsCreate, cancellationToken);
        var payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);

        if (payee is null)
            return Result<PlanAssignmentDto>.Failure("Payee not found.");

        var plan = await db.CompensationPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
            return Result<PlanAssignmentDto>.Failure("Plan not found.");

        // Money guard: an ARCHIVED plan is retired and must never gain a new assignment.
        //
        // Archiving already deactivates the assignments that exist at that moment (ArchivePlanHandler),
        // but that sweep cannot cover one created afterwards — and the engine's eligibility check used
        // to look only at the assignment's status, so a fresh Active assignment on a retired plan would
        // have been credited. That is not hypothetical: on 2026-07-22 an assignment that outlived its
        // plan's archiving let the CRM sync allocate €25,560 against a retired plan.
        //
        // Draft is deliberately still allowed: a plan is assigned while being prepared and activated
        // afterwards, which is the normal setup order. Only Archived is a closed door.
        if (plan.Status == PlanStatus.Archived)
            return Result<PlanAssignmentDto>.Failure(
                $"Plan '{plan.Name}' is archived and cannot be assigned to a payee. " +
                "Assign an active plan, or create a new version of this one.");

        // Integrity guard: the assignment period must match the plan's effective period exactly.
        // The UI locks this field, but a direct API call must not be able to misalign the period
        // against which attainment is measured.
        if (request.EffectiveStart != plan.EffectivePeriod.Start ||
            request.EffectiveEnd != plan.EffectivePeriod.End)
            return Result<PlanAssignmentDto>.Failure(
                "The assignment period must match the selected plan's effective period.");

        var payeeSnapshot = PayeeReference.Snapshot(payee.Id, payee.FullName, payee.EmployeeCode);
        var period = DateRange.Of(request.EffectiveStart, request.EffectiveEnd);

        var assignment = PlanAssignment.Create(
            tenantContext.TenantId,
            request.PlanId,
            request.PayeeId,
            payeeSnapshot,
            period,
            currentUser.UserId ?? "system",
            guid.NewGuid(),
            clock.UtcNowOffset,
            guid.NewGuid(),
            request.Notes);

        db.PlanAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return Result<PlanAssignmentDto>.Success(new PlanAssignmentDto(
            assignment.Id,
            assignment.TenantId,
            assignment.PlanId,
            plan.Name,
            plan.Version,
            assignment.PayeeId,
            assignment.PayeeSnapshot.FullName,
            assignment.PayeeSnapshot.EmployeeCode,
            assignment.EffectivePeriod.Start,
            assignment.EffectivePeriod.End,
            assignment.Status.ToString(),
            assignment.Notes,
            assignment.CreatedAt));
    }
}
