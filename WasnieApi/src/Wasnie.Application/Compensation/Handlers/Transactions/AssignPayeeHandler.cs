using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class AssignPayeeHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<AssignPayeeCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(AssignPayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsUpdate, cancellationToken);

        var transaction = await db.CompensationTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken);
        if (transaction is null) return Result<TransactionDto>.Failure("Transaction not found.");

        var payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null) return Result<TransactionDto>.Failure("Payee not found.");

        try
        {
            transaction.Assign(
                payeeId: request.PayeeId,
                comment: request.Comment,
                updatedBy: currentUser.UserId ?? "system",
                now: clock.UtcNowOffset,
                eventId: guid.NewGuid());
        }
        catch (DomainException ex)
        {
            return Result<TransactionDto>.Failure(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        request.AuditResourceId = transaction.Id.ToString();

        return Result<TransactionDto>.Success(IngestTransactionHandler.ToDto(transaction));
    }
}
