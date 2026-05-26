using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class UpdatePayeeHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePayeeCommand, Result<PayeeDto>>
{
    public async Task<Result<PayeeDto>> Handle(UpdatePayeeCommand request, CancellationToken cancellationToken)
    {
        var payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);

        if (payee is null)
            return Result<PayeeDto>.Failure("Payee not found.");

        var codeConflict = await db.Payees
            .AnyAsync(p => p.EmployeeCode == request.EmployeeCode && p.Id != request.PayeeId, cancellationToken);

        if (codeConflict)
            return Result<PayeeDto>.Failure($"Employee code '{request.EmployeeCode}' is already in use.");

        if (request.ManagerId.HasValue)
        {
            var managerExists = await db.Payees
                .AnyAsync(p => p.Id == request.ManagerId.Value, cancellationToken);
            if (!managerExists)
                return Result<PayeeDto>.Failure("Manager not found.");
        }

        payee.Update(
            request.FullName,
            request.EmployeeCode,
            request.Email,
            request.HireDate,
            request.Role,
            request.ManagerId,
            currentUser.UserId ?? "system");

        await db.SaveChangesAsync(cancellationToken);

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
