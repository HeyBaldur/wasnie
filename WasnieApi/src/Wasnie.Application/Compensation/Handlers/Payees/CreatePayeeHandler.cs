using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class CreatePayeeHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IRequestHandler<CreatePayeeCommand, Result<PayeeDto>>
{
    public async Task<Result<PayeeDto>> Handle(CreatePayeeCommand request, CancellationToken cancellationToken)
    {
        var codeExists = await db.Payees
            .AnyAsync(p => p.EmployeeCode == request.EmployeeCode, cancellationToken);

        if (codeExists)
            return Result<PayeeDto>.Failure($"Employee code '{request.EmployeeCode}' is already in use.");

        if (request.ManagerId.HasValue)
        {
            var managerExists = await db.Payees
                .AnyAsync(p => p.Id == request.ManagerId.Value, cancellationToken);
            if (!managerExists)
                return Result<PayeeDto>.Failure("Manager not found.");
        }

        var payee = Payee.Create(
            tenantContext.TenantId,
            request.FullName,
            request.EmployeeCode,
            request.Email,
            request.HireDate,
            currentUser.UserId ?? "system",
            request.Role,
            request.ManagerId);

        db.Payees.Add(payee);
        await db.SaveChangesAsync(cancellationToken);

        string? managerName = null;
        string? managerCode = null;
        if (payee.ManagerId.HasValue)
        {
            var manager = await db.Payees.FirstOrDefaultAsync(p => p.Id == payee.ManagerId.Value, cancellationToken);
            managerName = manager?.FullName;
            managerCode = manager?.EmployeeCode;
        }

        return Result<PayeeDto>.Success(ToDto(payee, 0, managerName, managerCode));
    }

    internal static PayeeDto ToDto(Payee payee, int activeAssignmentCount, string? managerName, string? managerCode) =>
        new(
            payee.Id,
            payee.TenantId,
            payee.FullName,
            payee.EmployeeCode,
            payee.Email,
            payee.Role,
            payee.ManagerId,
            managerName,
            managerCode,
            payee.HireDate,
            payee.TerminationDate,
            payee.Status,
            payee.Status.ToString(),
            activeAssignmentCount,
            payee.CreatedAt,
            payee.UpdatedAt);
}
