using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.DTOs;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// The payees the invite form may attach somebody to (KAN-92).
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
    public async Task<Result<UnlinkedPayeesResponse>> Handle(
        ListUnlinkedPayeesQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersManage, cancellationToken);

        var payees = await db.Payees
            .Where(p => p.UserId == null)
            .OrderBy(p => p.FullName)
            .Select(p => new UnlinkedPayeeDto(p.Id, p.FullName, p.EmployeeCode, p.Email))
            .ToListAsync(cancellationToken);

        UnlinkedPayeeDto? suggested = null;

        if (!string.IsNullOrWhiteSpace(request.EmailHint))
        {
            var hint = request.EmailHint.Trim();

            // Matched in memory over a list already fetched: no second round trip, and no index to
            // worry about. Case-insensitive, because an address typed by a human rarely matches the
            // stored one exactly.
            suggested = payees.FirstOrDefault(p =>
                p.Email is not null &&
                string.Equals(p.Email, hint, StringComparison.OrdinalIgnoreCase));
        }

        return Result<UnlinkedPayeesResponse>.Success(
            new UnlinkedPayeesResponse(payees, suggested));
    }
}
