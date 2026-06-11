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

public sealed class VoidTransactionHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<VoidTransactionCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(VoidTransactionCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsVoid, cancellationToken);

        var transaction = await db.CompensationTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken);
        if (transaction is null)
            return Result<TransactionDto>.Failure("Transaction not found.");

        var activeCredits = await db.Credits
            .Where(c => c.TransactionId == request.TransactionId && c.SupersededAt == null)
            .AnyAsync(cancellationToken);
        if (activeCredits)
            return Result<TransactionDto>.Failure("Cannot void a transaction that has active credits. Reassign or supersede the credits first.");

        try
        {
            transaction.Cancel(
                reason: request.Reason,
                cancelledBy: currentUser.UserId ?? "system",
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
