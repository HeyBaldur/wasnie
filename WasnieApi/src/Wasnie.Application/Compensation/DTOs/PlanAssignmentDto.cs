namespace Wasnie.Application.Compensation.DTOs;

public sealed record PlanAssignmentDto(
    Guid Id,
    Guid TenantId,
    Guid PlanId,
    string PlanName,
    int PlanVersion,
    Guid PayeeId,
    string PayeeFullName,
    string PayeeEmployeeCode,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt);
