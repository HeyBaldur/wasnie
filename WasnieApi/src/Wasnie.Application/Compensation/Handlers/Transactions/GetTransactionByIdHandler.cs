using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class GetTransactionByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetTransactionByIdQuery, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(
        GetTransactionByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsRead, cancellationToken);

        var tx = await db.CompensationTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken);

        if (tx is null)
            return Result<TransactionDto>.Failure("Transaction not found.");

        return Result<TransactionDto>.Success(IngestTransactionHandler.ToDto(tx));
    }
}
