using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Commands.Quotas;

/// <summary>
/// One quota configuration applied to N payees. Every field except <see cref="PayeeIds"/> is the same
/// for every quota created — the whole point is that the admin describes the target once.
///
/// Shaped as a superset of <see cref="CreateQuotaCommand"/> on purpose: one payee id becomes a list,
/// nothing else changes, so there is no second way to describe a quota.
/// </summary>
public sealed record BulkCreateQuotasCommand(
    IReadOnlyList<Guid> PayeeIds,
    Guid PlanId,
    QuotaMeasurementType MeasurementType,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Notes = null) : IRequest<Result<BulkCreateQuotasResultDto>>;
