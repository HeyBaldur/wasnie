using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Transactions;

public sealed record ListTransactionsQuery(PaginationQuery Pagination) : IRequest<Result<PagedResult<TransactionDto>>>;
