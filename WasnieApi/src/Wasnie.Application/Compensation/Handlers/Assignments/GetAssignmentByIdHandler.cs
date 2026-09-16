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

        // Current payee name, not the snapshot — see ListAssignmentsHandler.
        var result = await (
                from a in db.PlanAssignments
                join pl in db.CompensationPlans on a.PlanId equals pl.Id
                join py in db.Payees on a.PayeeId equals py.Id into payees
                from py in payees.DefaultIfEmpty()
                select new
                {
                    Assignment = a,
                    PlanName = pl.Name,
                    PlanVersion = pl.Version,
                    PayeeFullName = py != null ? py.FullName : a.PayeeSnapshot.FullName,
                    PayeeEmployeeCode = py != null ? py.EmployeeCode : a.PayeeSnapshot.EmployeeCode,
                })
            .Where(x => x.Assignment.Id == request.AssignmentId)
            .Select(x => new PlanAssignmentDto(
                x.Assignment.Id,
                x.Assignment.TenantId,
                x.Assignment.PlanId,
                x.PlanName,
                x.PlanVersion,
                x.Assignment.PayeeId,
                x.PayeeFullName,
                x.PayeeEmployeeCode,
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
