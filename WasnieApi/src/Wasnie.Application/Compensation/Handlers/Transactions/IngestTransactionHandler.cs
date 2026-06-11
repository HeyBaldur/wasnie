using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Constants;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class IngestTransactionHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService,
    IFieldRequirementService fieldRequirements,
    ICreditAllocationService creditAllocationService)
    : IRequestHandler<IngestTransactionCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(IngestTransactionCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsCreate, cancellationToken);

        var payeeIdRequired = await fieldRequirements.IsRequiredAsync(
            TransactionFieldNames.Entity, TransactionFieldNames.PayeeId, cancellationToken);

        if (request.PayeeId is null)
        {
            if (payeeIdRequired)
                return Result<TransactionDto>.Failure("Payee is required for this tenant. Please assign a payee.");
        }
        else
        {
            var payee = await db.Payees
                .FirstOrDefaultAsync(p => p.Id == request.PayeeId.Value, cancellationToken);

            if (payee is null)
                return Result<TransactionDto>.Failure($"Payee '{request.PayeeId}' not found.");

            if (!payee.IsActive)
                return Result<TransactionDto>.Failure($"Payee '{request.PayeeId}' is inactive. Use import to assign historical transactions to inactive payees.");
        }

        Money amount;
        try
        {
            amount = Money.Of(request.Amount, request.Currency);
        }
        catch (DomainException ex)
        {
            return Result<TransactionDto>.Failure(ex.Message);
        }

        var txId = guid.NewGuid();
        var now = clock.UtcNowOffset;

        var tx = CompensationTransaction.Ingest(
            tenantId: tenantContext.TenantId,
            referenceNumber: request.ReferenceNumber,
            payeeId: request.PayeeId,
            amount: amount,
            transactionDate: request.TransactionDate,
            source: TransactionSource.Manual,
            ingestedBy: currentUser.UserId ?? "system",
            id: txId,
            now: now,
            eventId: guid.NewGuid(),
            quantity: request.Quantity);

        db.CompensationTransactions.Add(tx);
        await db.SaveChangesAsync(cancellationToken);

        if (request.ProcessImmediately)
        {
            // Allocate credits immediately after ingest (Decision #44: null payee = no credits).
            var credits = await creditAllocationService.AllocateAsync(tx, cancellationToken);
            foreach (var credit in credits)
                db.Credits.Add(credit);

            if (credits.Count > 0)
            {
                var total = credits.Skip(1).Aggregate(credits[0].CreditedAmount,
                    (acc, c) => acc.Add(c.CreditedAmount));
                tx.MarkCalculated(credits.Count, total, currentUser.UserId ?? "system",
                    clock.UtcNowOffset, guid.NewGuid());
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        request.AuditResourceId = tx.Id.ToString();

        return Result<TransactionDto>.Success(ToDto(tx));
    }

    internal static TransactionDto ToDto(CompensationTransaction tx) =>
        new(tx.Id, tx.TenantId, tx.ReferenceNumber, tx.PayeeId,
            tx.Amount.Amount, tx.Amount.Currency, tx.Quantity, tx.TransactionDate,
            tx.Source.ToString(), tx.Status.ToString(), tx.ExternalId,
            tx.IngestedAt, tx.IngestedBy, tx.UpdatedAt,
            CancelledBy: tx.CancelledBy,
            CancelledAt: tx.CancelledAt,
            CancelledReason: tx.CancelledReason);
}
