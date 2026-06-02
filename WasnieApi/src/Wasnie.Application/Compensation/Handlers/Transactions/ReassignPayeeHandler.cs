using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
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
    IAuthorizationService authorizationService,
    ICreditAllocationService creditAllocationService)
    : IRequestHandler<ReassignPayeeCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(ReassignPayeeCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsUpdate, cancellationToken);

        var transaction = await db.CompensationTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken);
        if (transaction is null) return Result<TransactionDto>.Failure("Transaction not found.");

        var newPayee = await db.Payees
            .FirstOrDefaultAsync(p => p.Id == request.NewPayeeId, cancellationToken);
        if (newPayee is null) return Result<TransactionDto>.Failure("Payee not found.");

        var now = clock.UtcNowOffset;
        var userId = currentUser.UserId ?? "system";

        // Decision #46 Case A: supersede any active Credits before changing the payee.
        // Captures old payee ID before Reassign modifies the transaction.
        var oldPayeeId = transaction.PayeeId;
        var existingCredits = await db.Credits
            .Where(c => c.TransactionId == request.TransactionId && c.SupersededAt == null)
            .ToListAsync(cancellationToken);

        if (existingCredits.Count > 0)
        {
            var supersedeReason = BuildSupersedeReason(oldPayeeId, request.NewPayeeId, userId, now, request.Reason);
            foreach (var credit in existingCredits)
                credit.Supersede(supersedeReason, now, guid.NewGuid());
        }

        try
        {
            transaction.Reassign(
                newPayeeId: request.NewPayeeId,
                reason: request.Reason,
                updatedBy: userId,
                now: now,
                eventId: guid.NewGuid());
        }
        catch (DomainException ex)
        {
            return Result<TransactionDto>.Failure(ex.Message);
        }

        // Option A per Decision #46: re-allocate immediately so the transaction reaches a clean state.
        // After Reassign the transaction is Pending; AllocateAsync will produce Credits if the new
        // payee has an active PlanAssignment covering the transaction date.
        var newCredits = await creditAllocationService.AllocateAsync(transaction, cancellationToken);
        foreach (var credit in newCredits)
            db.Credits.Add(credit);

        if (newCredits.Count > 0)
        {
            var total = newCredits.Skip(1).Aggregate(newCredits[0].CreditedAmount,
                (acc, c) => acc.Add(c.CreditedAmount));
            transaction.MarkCalculated(newCredits.Count, total, userId, now, guid.NewGuid());
        }

        await db.SaveChangesAsync(cancellationToken);
        request.AuditResourceId = transaction.Id.ToString();

        return Result<TransactionDto>.Success(IngestTransactionHandler.ToDto(transaction));
    }

    private static string BuildSupersedeReason(
        Guid? oldPayeeId, Guid newPayeeId, string userId, DateTimeOffset timestamp, string reassignReason)
    {
        var raw = $"Reassigned from payee {oldPayeeId} to payee {newPayeeId} by {userId} at {timestamp:O}. Reason: {reassignReason}";
        return raw.Length > 500 ? raw[..500] : raw;
    }
}
