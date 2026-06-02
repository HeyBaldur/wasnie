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
using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Application.Compensation.Handlers.Payees;

public sealed class CreatePayeeHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuditService auditService,
    IAuthorizationService authorizationService,
    ITierLimitChecker tierLimitChecker)
    : IRequestHandler<CreatePayeeCommand, Result<PayeeDto>>
{
    public async Task<Result<PayeeDto>> Handle(CreatePayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayeesCreate, cancellationToken);
        await tierLimitChecker.EnsurePayeeLimitAsync(cancellationToken);

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

        Domain.Compensation.Payees.EmploymentType? employmentType = null;
        if (request.EmploymentType is not null &&
            Enum.TryParse<Domain.Compensation.Payees.EmploymentType>(request.EmploymentType, ignoreCase: true, out var et))
            employmentType = et;

        var payee = Payee.Create(
            tenantContext.TenantId,
            request.FullName,
            request.EmployeeCode,
            request.Email,
            request.HireDate,
            currentUser.UserId ?? "system",
            guid.NewGuid(),
            clock.UtcNowOffset,
            request.Role,
            request.ManagerId,
            employmentType,
            request.Location);

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

        var dto = ToDto(payee, 0, managerName, managerCode);

        try
        {
            await auditService.LogAsync(new AuditEntry(
                TenantId: tenantContext.TenantId,
                Action: AuditActions.PayeeCreated,
                ResourceType: ResourceTypes.Payee,
                ResourceId: payee.Id.ToString(),
                ActorUserId: currentUser.UserId ?? "system",
                ActorEmail: currentUser.Email ?? string.Empty,
                DisplayName: payee.FullName,
                After: EntityDiff.Serialize(dto)), cancellationToken);
        }
        catch { /* audit failures must not block the user operation */ }

        return Result<PayeeDto>.Success(dto);
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
            payee.UpdatedAt,
            payee.EmploymentType?.ToString(),
            payee.Location,
            payee.IsActive,
            payee.DeactivatedAt);
}
