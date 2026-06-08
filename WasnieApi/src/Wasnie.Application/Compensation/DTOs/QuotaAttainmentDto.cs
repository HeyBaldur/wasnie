using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.DTOs;

public sealed record QuotaAttainmentDto(
    Guid QuotaId,
    Guid PlanId,
    string PlanName,
    QuotaMeasurementType MeasurementType,
    decimal TargetAmount,
    string Currency,
    decimal AchievedAmount,
    decimal AttainmentValue,
    string AttainmentPercent,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    bool IsCurrencyValid,
    string PlanCurrency);
