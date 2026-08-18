using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// The payee's ledger, newest first. Every entry ever written is returned — an append-only record
/// that hides rows is not an audit trail, so there is no filter that removes history.
/// </summary>
public sealed class ListPayeeLedgerEntriesHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<ListPayeeLedgerEntriesQuery, Result<IReadOnlyList<PayeeLedgerEntryDto>>>
{
    public async Task<Result<IReadOnlyList<PayeeLedgerEntryDto>>> Handle(
        ListPayeeLedgerEntriesQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.LedgerRead, cancellationToken);

        // ★ THE REFUSAL HAD TO GAIN A NOT-FOUND CASE TO STAY INDISTINGUISHABLE. This endpoint used to
        // answer 200 + [] for an unknown payee, so refusing an existing-but-foreign payee with an error
        // would have told the caller "this id is real" — the enumeration oracle, rebuilt by accident
        // while closing the hole. Both cases now produce the SAME failure, so an empty list means one
        // thing only: a payee you may read, whose ledger has no entries.
        if (!await payeeAccessGuard.CanReadAsync(request.PayeeId, cancellationToken) ||
            !await db.Payees.AnyAsync(p => p.Id == request.PayeeId, cancellationToken))
        {
            return Result<IReadOnlyList<PayeeLedgerEntryDto>>.Failure(PayeeAccessDenied.Message);
        }

        var entries = await db.PayeeLedgerEntries
            .Where(e => e.PayeeId == request.PayeeId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new PayeeLedgerEntryDto(
                e.Id,
                e.CreatedAt,
                e.Origin.ToString(),
                e.TransactionType.ToString(),
                e.Amount.Amount,
                e.Amount.Currency,
                e.Justification,
                e.CreatedBy,
                e.SourceExternalDealId,
                e.SourceTransactionId,
                e.DaysActive,
                e.MaturationDays,
                e.SourceCommissionAmount,
                e.EventDate,
                e.SourcePlanId))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PayeeLedgerEntryDto>>.Success(entries);
    }
}
