using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Handlers.Reconciliation;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Application.Compensation.Queries.Reconciliation;
using Wasnie.Application.Compensation.Queries.Sidebar;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Sidebar;

/// <summary>
/// The sidebar's counts.
///
/// ★★ IT COUNTS NOTHING OF ITS OWN. Both figures come from the queries the screens themselves run —
/// <see cref="ReconciliationQuery.Filtered"/> and the terminated-accounts query — so a badge cannot
/// disagree with the page it links to. Writing "SELECT COUNT(*) WHERE …" here, or in a stored
/// procedure, would be a second definition of which money is unpayable: the Reconciliation Centre's
/// own rule changed twice in a single day (closures, then fact keys), and a copy would have been
/// wrong within hours while still looking authoritative.
///
/// ★★ EVERY BADGE IS GATED, AND A REFUSAL IS null RATHER THAN 0. `HasAsync`, not `RequireAsync`: a
/// user who may see one of the two must still get the other, so this endpoint never 403s as a whole.
/// A missing permission yields null, which the sidebar draws as no badge — a 0 would be a claim about
/// the tenant's money that this user was not cleared to receive.
/// </summary>
public sealed class GetSidebarBadgesHandler(
    IApplicationDbContext db,
    ISender sender,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetSidebarBadgesQuery, Result<SidebarBadgesDto>>
{
    public async Task<Result<SidebarBadgesDto>> Handle(
        GetSidebarBadgesQuery request, CancellationToken ct)
    {
        int? reconciliation = null;
        int? terminated = null;

        if (await authorizationService.HasAsync(Permission.ReportsViewAll, ct))
        {
            // ★ THE SAME EXPRESSION THE CENTRE PAGES OVER, counted the same way its own summary counts
            // it: DISTINCT over (kind, entity), because an entry failing for two reasons is one row.
            // An unfiltered ReconciliationFilter is the whole open queue — closed rows are already
            // excluded inside Filtered().
            reconciliation = await ReconciliationQuery
                .Filtered(db, new ReconciliationFilter())
                .Select(s => new { s.Kind, s.EntityId })
                .Distinct()
                .CountAsync(ct);
        }

        if (await authorizationService.HasAsync(Permission.LedgerRead, ct))
        {
            // ★★ THE HANDLER, NOT A COPY OF ITS PREDICATE. This queue is not "terminated payees": it is
            // one row per (payee, currency), assembled from balances AND unsettled credits, and then
            // filtered by PayeeAccessGuard so a rep sees only their own. Re-deriving any of that here
            // would eventually disagree with the screen — and the RBAC half would disagree silently,
            // which is the expensive kind.
            var result = await sender.Send(new ListTerminatedPayeesWithBalanceQuery(), ct);

            // A refusal from the inner query leaves the badge absent rather than taking the whole
            // sidebar down: the other count is still worth delivering.
            terminated = result.IsSuccess ? result.Value!.Rows.Count : null;
        }

        return Result<SidebarBadgesDto>.Success(new SidebarBadgesDto(
            Reconciliation: reconciliation,
            TerminatedAccounts: terminated,
            FinancialsTotal: (reconciliation ?? 0) + (terminated ?? 0)));
    }
}
