using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Quotas;

public sealed record CreateQuotaCommand(
    Guid PayeeId,
    Guid PlanId,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd) : IRequest<Result<QuotaDto>>;
