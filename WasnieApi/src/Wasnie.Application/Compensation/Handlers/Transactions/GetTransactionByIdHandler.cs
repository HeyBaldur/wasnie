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

        var dto = IngestTransactionHandler.ToDto(tx);

        // The list enriches each row with the payee's name/code from the Payees table; the single-get
        // must do the same, or the detail screen shows "Unassigned" for a transaction that clearly has a
        // payee in the list. (Same tenant, so the global query filter scopes the lookup.)
        if (tx.PayeeId is not null)
        {
            var payee = await db.Payees
                .Where(p => p.Id == tx.PayeeId.Value)
                .Select(p => new { p.FullName, p.EmployeeCode })
                .FirstOrDefaultAsync(cancellationToken);

            if (payee is not null)
                dto = dto with { PayeeName = payee.FullName, PayeeEmployeeCode = payee.EmployeeCode };
        }

        return Result<TransactionDto>.Success(dto);
    }
}
