using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Plans;

public sealed record ListPlansQuery(PaginationQuery Pagination) : IRequest<Result<PagedResult<PlanSummaryDto>>>;
