using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class GetPayeeByIdHandler(IApplicationDbContext db)
    : IRequestHandler<GetPayeeByIdQuery, Result<PayeeDto>>
{
    public async Task<Result<PayeeDto>> Handle(GetPayeeByIdQuery request, CancellationToken cancellationToken)
    {
        var payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);

        if (payee is null)
            return Result<PayeeDto>.Failure("Payee not found.");

        var activeAssignments = await db.PlanAssignments
            .CountAsync(a => a.PayeeId == payee.Id && a.Status == AssignmentStatus.Active, cancellationToken);

        string? managerName = null;
        string? managerCode = null;
        if (payee.ManagerId.HasValue)
        {
            var manager = await db.Payees.FirstOrDefaultAsync(p => p.Id == payee.ManagerId.Value, cancellationToken);
            managerName = manager?.FullName;
            managerCode = manager?.EmployeeCode;
        }

        return Result<PayeeDto>.Success(CreatePayeeHandler.ToDto(payee, activeAssignments, managerName, managerCode));
    }
}
