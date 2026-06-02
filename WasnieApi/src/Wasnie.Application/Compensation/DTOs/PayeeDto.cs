using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Application.Compensation.DTOs;

public sealed record PayeeDto(
    Guid Id,
    Guid TenantId,
    string FullName,
    string EmployeeCode,
    string? Email,
    string? Role,
    Guid? ManagerId,
    string? ManagerName,
    string? ManagerEmployeeCode,
    DateOnly? HireDate,
    DateOnly? TerminationDate,
    PayeeStatus Status,
    string StatusLabel,
    int ActiveAssignmentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? EmploymentType = null,
    string? Location = null,
    bool IsActive = true,
    DateTimeOffset? DeactivatedAt = null);
