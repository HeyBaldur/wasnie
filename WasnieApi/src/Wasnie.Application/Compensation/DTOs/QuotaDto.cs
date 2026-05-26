using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.DTOs;

public sealed record QuotaDto(
    Guid Id,
    Guid TenantId,
    Guid PayeeId,
    Guid PlanId,
    QuotaMeasurementType MeasurementType,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt);
