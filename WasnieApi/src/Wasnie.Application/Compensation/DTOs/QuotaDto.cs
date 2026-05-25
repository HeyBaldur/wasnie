namespace Wasnie.Application.Compensation.DTOs;

public sealed record QuotaDto(
    Guid Id,
    Guid TenantId,
    Guid PayeeId,
    Guid PlanId,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    DateTimeOffset CreatedAt);
