using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Plans;

public sealed record ListPlansQuery(string? StatusFilter = null) : IRequest<Result<IList<PlanSummaryDto>>>;
