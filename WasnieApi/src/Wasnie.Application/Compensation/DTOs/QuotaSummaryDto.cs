using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.DTOs;

public sealed record QuotaSummaryDto(
    Guid Id,
    Guid TenantId,
    Guid PayeeId,
    string PayeeName,
    string PayeeEmployeeCode,
    Guid PlanId,
    string PlanName,
    QuotaMeasurementType MeasurementType,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    bool IsCurrencyValid = true,
    string PlanCurrency = "");
