using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

public sealed class GetAssignmentByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<GetAssignmentByIdQuery, Result<PlanAssignmentDto>>
{
    public async Task<Result<PlanAssignmentDto>> Handle(GetAssignmentByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.AssignmentsRead, cancellationToken);

        var result = await db.PlanAssignments
            .Join(db.CompensationPlans,
                a => a.PlanId,
                pl => pl.Id,
                (a, pl) => new { Assignment = a, PlanName = pl.Name, PlanVersion = pl.Version })
            .Where(x => x.Assignment.Id == request.AssignmentId)
            .Select(x => new PlanAssignmentDto(
                x.Assignment.Id,
                x.Assignment.TenantId,
                x.Assignment.PlanId,
                x.PlanName,
                x.PlanVersion,
                x.Assignment.PayeeId,
                x.Assignment.PayeeSnapshot.FullName,
                x.Assignment.PayeeSnapshot.EmployeeCode,
                x.Assignment.EffectivePeriod.Start,
                x.Assignment.EffectivePeriod.End,
                x.Assignment.Status.ToString(),
                x.Assignment.Notes,
                x.Assignment.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
            return Result<PlanAssignmentDto>.Failure(PayeeAccessDenied.AssignmentMessage);

        // Same shape as GetQuotaById: the owner is only known after the row is loaded, so the guard
        // runs on the DTO's PayeeId and answers with the endpoint's own not-found sentence.
        if (!await payeeAccessGuard.CanReadAsync(result.PayeeId, cancellationToken))
            return Result<PlanAssignmentDto>.Failure(PayeeAccessDenied.AssignmentMessage);

        return Result<PlanAssignmentDto>.Success(result);
    }
}
