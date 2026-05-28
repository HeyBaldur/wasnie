using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Transactions;

public sealed record GetTransactionByIdQuery(Guid TransactionId) : IRequest<Result<TransactionDto>>;
