using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
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
    IAuthorizationService authorizationService)
    : IRequestHandler<IngestTransactionCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(IngestTransactionCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsCreate, cancellationToken);

        var payeeExists = await db.Payees
            .AnyAsync(p => p.Id == request.PayeeId, cancellationToken);

        if (!payeeExists)
            return Result<TransactionDto>.Failure($"Payee '{request.PayeeId}' not found.");

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
            eventId: guid.NewGuid());

        db.CompensationTransactions.Add(tx);
        await db.SaveChangesAsync(cancellationToken);

        request.AuditResourceId = tx.Id.ToString();

        return Result<TransactionDto>.Success(ToDto(tx));
    }

    internal static TransactionDto ToDto(CompensationTransaction tx) =>
        new(tx.Id, tx.TenantId, tx.ReferenceNumber, tx.PayeeId,
            tx.Amount.Amount, tx.Amount.Currency, tx.TransactionDate,
            tx.Source.ToString(), tx.Status.ToString(), tx.ExternalId,
            tx.IngestedAt, tx.IngestedBy, tx.UpdatedAt);
}
