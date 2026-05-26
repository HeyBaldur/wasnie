using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Quotas;

public sealed record ListQuotasByPayeeQuery(Guid PayeeId, PaginationQuery Pagination) : IRequest<Result<PagedResult<QuotaSummaryDto>>>;
