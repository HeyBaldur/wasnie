using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// The accounts nobody is processing any more: payees who have LEFT and whose balance is not zero.
///
/// Terminating someone takes them out of every future pay run — which is right, because they will earn
/// nothing more and a ghost in the engine is worse than useless. But a frozen debt that nobody can see
/// is how debt quietly evaporates, so the freeze and this list ship together: the engine stops, finance
/// gets a work queue, and the account is closed by a person with
/// <c>ExternalSettlementCredit</c> (recovered elsewhere) or <c>WriteOffCredit</c> (absorbed as a loss).
///
/// Wasnie neither collects the money nor decides which of the two it is; it makes the open account
/// impossible to overlook and records the decision that finance makes.
/// </summary>
public sealed class ListTerminatedPayeesWithBalanceHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<ListTerminatedPayeesWithBalanceQuery, Result<IReadOnlyList<TerminatedPayeeBalanceDto>>>
{
    public async Task<Result<IReadOnlyList<TerminatedPayeeBalanceDto>>> Handle(
        ListTerminatedPayeesWithBalanceQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.LedgerRead, cancellationToken);

        // A balance row exists per (payee, currency), so a payee owing EUR and owed USD legitimately
        // appears twice — one open account per currency, because Wasnie holds no exchange rates and
        // must never present a single blended figure.
        var rows = await (
            from b in db.PayeeBalances
            join p in db.Payees on b.PayeeId equals p.Id
            where p.Status == PayeeStatus.Terminated && b.Balance.Amount != 0m
            orderby b.Balance.Amount   // deepest debt first: the largest exposure is the first thing seen
            select new TerminatedPayeeBalanceDto(
                p.Id,
                p.FullName,
                p.EmployeeCode,
                p.TerminationDate,
                b.Balance.Amount,
                b.Currency,
                b.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<TerminatedPayeeBalanceDto>>.Success(rows);
    }
}
