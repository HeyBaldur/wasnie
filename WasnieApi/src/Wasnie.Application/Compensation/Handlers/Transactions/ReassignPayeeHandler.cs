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

public sealed class ReassignPayeeHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<ReassignPayeeCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(ReassignPayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsUpdate, cancellationToken);

        var transaction = await db.CompensationTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken);
        if (transaction is null) return Result<TransactionDto>.Failure("Transaction not found.");

        var payee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == request.NewPayeeId, cancellationToken);
        if (payee is null) return Result<TransactionDto>.Failure("Payee not found.");

        try
        {
            transaction.Reassign(
                newPayeeId: request.NewPayeeId,
                reason: request.Reason,
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
