using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Payees;

public sealed record ListPayeesQuery(PaginationQuery Pagination) : IRequest<Result<PagedResult<PayeeDto>>>;
