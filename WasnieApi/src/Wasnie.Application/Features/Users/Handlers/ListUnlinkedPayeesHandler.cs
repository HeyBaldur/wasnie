using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.DTOs;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// The payees a login may be attached to (KAN-92, made a typeahead by KAN-93).
///
/// ★★ IT ANSWERS "WHO HAS NOBODY YET", NOT "WHO IS THERE". Every payee already owned by a login is
/// left out, because choosing one would MOVE who can see that person's pay — a deliberate act, never
/// a side effect of sending an invitation. The invite handler refuses a taken payee for the same
/// reason; this list means the administrator never has to be refused.
///
/// ★★ THE EMAIL MATCH IS RETURNED SEPARATELY AND IS NEVER APPLIED. Addresses coincide for boring
/// reasons — a shared mailbox, a replacement in the same seat, two people at one small firm — and a
/// match accepted by reflex hands somebody another person's pay. So the server says "this one looks
/// like it" and stops; the administrator is the one who decides.
///
/// ★ IT IS GUARDED BY Users.Manage, NOT Payees.Read. This is a list of people with their names and
/// addresses, offered while granting access; the narrower of the two permissions is the right one.
/// </summary>
public sealed class ListUnlinkedPayeesHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<ListUnlinkedPayeesQuery, Result<UnlinkedPayeesResponse>>
{
    /// <summary>
    /// How many names a picker gets back at once.
    ///
    /// ★ THE SAME 20 EVERY OTHER PAYEE PICKER IN THIS PRODUCT USES (quota-create, credits, payouts,
    /// pay-runs, the payee form's manager field). A dropdown is a shortlist to recognise a name in,
    /// not a roster to scroll: past twenty rows people type instead, which is what the search is for.
    /// </summary>
    private const int PickerLimit = 20;

    public async Task<Result<UnlinkedPayeesResponse>> Handle(
        ListUnlinkedPayeesQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var unlinked = db.Payees.Where(p => p.UserId == null);

        // ★★ THE TOTAL IS COUNTED BEFORE THE SEARCH, AND IT IS NOT A STATISTIC. It is the only thing
        // that keeps "every payee here already belongs to somebody" — which an administrator fixes by
        // unlinking one — distinguishable from "your search matched nothing", which they fix by typing
        // something else. Derived from the filtered rows instead, an empty page would say the first
        // when it meant the second, and send them off to detach a link that was never the problem (§B3).
        var totalAvailable = await unlinked.CountAsync(cancellationToken);

        var matching = unlinked;

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Matched in the DATABASE, not in memory. The whole point of this change is that the
            // thousand rows never travel; filtering a list we already fetched would keep the cost and
            // only hide it. Name, code and address, because an administrator attaching somebody knows
            // one of the three and not reliably which.
            var q = request.Search.Trim().ToLower();
            matching = matching.Where(p =>
                p.FullName.ToLower().Contains(q) ||
                p.EmployeeCode.ToLower().Contains(q) ||
                (p.Email != null && p.Email.ToLower().Contains(q)));
        }

        var payees = await matching
            .OrderBy(p => p.FullName)
            .Take(PickerLimit)
            .Select(p => new UnlinkedPayeeDto(p.Id, p.FullName, p.EmployeeCode, p.Email))
            .ToListAsync(cancellationToken);

        // ★★ THE SUGGESTION IS ITS OWN QUERY, AND MAKING IT ONE IS THE WHOLE RISK OF THIS CHANGE. It
        // used to be matched in memory over a list that held EVERY unlinked payee, so it always found
        // its man. The moment that list is capped at twenty and narrowed by a search box, the same
        // in-memory match silently stops working for any workspace bigger than the cap — and it stops
        // working QUIETLY: no error, just an administrator who never sees "a payee with this address
        // exists" and links the wrong person by hand. So it is asked of the database, by address, and
        // it deliberately ignores both the search text and the limit: what the admin is typing into
        // the dropdown has nothing to do with which record matches the invitee's address.
        UnlinkedPayeeDto? suggested = null;

        if (!string.IsNullOrWhiteSpace(request.EmailHint))
        {
            var hint = request.EmailHint.Trim().ToLower();

            suggested = await unlinked
                .Where(p => p.Email != null && p.Email.ToLower() == hint)
                .OrderBy(p => p.FullName)
                .Select(p => new UnlinkedPayeeDto(p.Id, p.FullName, p.EmployeeCode, p.Email))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return Result<UnlinkedPayeesResponse>.Success(
            new UnlinkedPayeesResponse(payees, suggested, totalAvailable));
    }
}
