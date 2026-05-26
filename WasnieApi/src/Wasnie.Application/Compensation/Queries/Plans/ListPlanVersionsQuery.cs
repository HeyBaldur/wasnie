using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Plans;

public sealed record ListPlanVersionsQuery(string PlanName, PaginationQuery Pagination) : IRequest<Result<PagedResult<PlanSummaryDto>>>;
