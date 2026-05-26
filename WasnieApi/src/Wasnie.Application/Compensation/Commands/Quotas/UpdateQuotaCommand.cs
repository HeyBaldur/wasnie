using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Commands.Quotas;

public sealed record UpdateQuotaCommand(
    Guid QuotaId,
    QuotaMeasurementType MeasurementType,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Notes = null) : IRequest<Result<QuotaSummaryDto>>;
