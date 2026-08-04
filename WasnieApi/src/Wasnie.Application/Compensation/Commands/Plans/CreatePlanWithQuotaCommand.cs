using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Commands.Plans;

/// <summary>
/// One quota inside a <see cref="CreatePlanWithQuotaCommand"/>. Field for field the quota half of
/// <c>CreateQuotaCommand</c>, minus <c>PlanId</c> — the plan does not exist yet, and naming it would
/// be the one field a caller could get wrong in a way the handler would have to second-guess.
/// </summary>
public sealed record PlanQuotaSpec(
    Guid PayeeId,
    QuotaMeasurementType MeasurementType,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Notes = null);

/// <summary>
/// A plan and its quota(s), created together or not at all.
///
/// WHY IT EXISTS: a plan carrying an accelerator pays €0 until a quota gives it something to measure
/// attainment against. Creating the plan and then failing to create the quota leaves a plan that looks
/// configured and silently pays nothing — the worst kind of failure in this system, because it is
/// invisible until payday. So the two are one operation.
///
/// AGNOSTIC ABOUT ITS ORIGIN on purpose. This is not "the AI endpoint". It describes a plan with
/// quotas; whether the description came from an assistant, a web form or an external API is not
/// something the handler can tell, and nothing downstream branches on it. The day a second caller
/// appears, it needs no second endpoint.
///
/// A superset of <see cref="CreatePlanCommand"/> — the plan fields are identical and in the same
/// order, with a list of quotas appended. There is no second way to describe a plan.
/// </summary>
public sealed record CreatePlanWithQuotaCommand(
    string Name,
    string Description,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    string Currency,
    IReadOnlyList<PlanQuotaSpec> Quotas) : IRequest<Result<CreatePlanWithQuotaResultDto>>;
