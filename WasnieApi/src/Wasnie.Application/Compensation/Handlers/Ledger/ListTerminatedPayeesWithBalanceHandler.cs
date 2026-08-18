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
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<ListTerminatedPayeesWithBalanceQuery, Result<IReadOnlyList<TerminatedPayeeBalanceDto>>>
{
    public async Task<Result<IReadOnlyList<TerminatedPayeeBalanceDto>>> Handle(
        ListTerminatedPayeesWithBalanceQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.LedgerRead, cancellationToken);

        // ★ A LIST CANNOT BE PROTECTED BY A PER-RESOURCE CHECK — IT HAS TO BE FILTERED. This endpoint
        // takes no payee id: it IS the list of which payees to look at, which made it the widest leak
        // of the three. Every departed colleague's outstanding debt, in one call, to anyone holding
        // Ledger.Read (Rep included).
        //
        // FILTERED, NOT REFUSED. Trimming the rows to what the caller may see is both the safe answer
        // and the useful one: a manager legitimately sees a departed report here, and a wholesale 403
        // would take that away to no benefit. For a Rep the visible set is their own payee, so the
        // list is empty or their own closed account — nothing about anyone else, and no way to tell
        // an empty result from a tenant with no orphan accounts at all.
        var visibility = await payeeAccessGuard.GetVisibilityAsync(cancellationToken);

        // Null = no filter (supervisory role). An ARRAY rather than the set itself so EF translates the
        // membership test into a plain IN (...) instead of tripping over IReadOnlySet at query time.
        var visibleIds = visibility.IsUnrestricted ? null : visibility.PayeeIds.ToArray();

        // A balance row exists per (payee, currency), so a payee owing EUR and owed USD legitimately
        // appears twice — one open account per currency, because Wasnie holds no exchange rates and
        // must never present a single blended figure.
        var rows = await (
            from b in db.PayeeBalances
            join p in db.Payees on b.PayeeId equals p.Id
            where p.Status == PayeeStatus.Terminated && b.Balance.Amount != 0m
                  && (visibleIds == null || visibleIds.Contains(b.PayeeId))
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
