using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payees;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class UpdatePayeeHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    ITenantContext tenantContext,
    IAuditService auditService,
    IAuthorizationService authorizationService)
    : IRequestHandler<UpdatePayeeCommand, Result<PayeeDto>>
{
    public async Task<Result<PayeeDto>> Handle(UpdatePayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayeesUpdate, cancellationToken);

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

        var beforeSnapshot = new
        {
            payee.FullName,
            payee.EmployeeCode,
            payee.Email,
            payee.HireDate,
            payee.Role,
            payee.ManagerId,
        };

        payee.Update(
            request.FullName,
            request.EmployeeCode,
            request.Email,
            request.HireDate,
            request.Role,
            request.ManagerId,
            currentUser.UserId ?? "system",
            clock.UtcNowOffset);

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

        var dto = CreatePayeeHandler.ToDto(payee, activeAssignments, managerName, managerCode);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.PayeeUpdated,
                ResourceType: ResourceTypes.Payee,
                ResourceId: payee.Id.ToString(),
                ActorUserId: currentUser.UserId ?? "system",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: payee.FullName,
                Before: EntityDiff.Serialize(beforeSnapshot),
                After: EntityDiff.Serialize(dto)), cancellationToken);
        }
        catch { /* audit failures must not block the user operation */ }

        return Result<PayeeDto>.Success(dto);
    }
}
