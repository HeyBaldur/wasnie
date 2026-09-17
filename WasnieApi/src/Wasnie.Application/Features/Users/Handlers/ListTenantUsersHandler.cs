using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.DTOs;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Identity;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// The users screen in one response: people, outstanding invitations, and seats (KAN-32).
///
/// ★★ NOT PAGINATED, AND THAT IS A DECISION. Every other list in this product is paginated server-side
/// because it grows with the business; this one is the staff of one company using one tool, which is
/// tens of rows and does not grow with revenue. Paginating it would cost a search box, a page bar and
/// a second round trip to solve a problem nobody has. If a tenant ever arrives with hundreds of logins,
/// this is the comment that says the decision was made with a number in mind and can be revisited.
///
/// ★★ IT ASKS THE MEMBERSHIPS, NOT THE CLAIM (KAN-91). The first version read Identity's tenant_id
/// claim, which was right while a person could only belong to one workspace and became wrong the
/// moment they could belong to two: the claim holds the FIRST workspace somebody ever joined, so
/// anybody invited in from elsewhere was simply absent from the screen that manages them — while the
/// invite form refused their address as "already a member". Two truths, one screen, §B3.
/// </summary>
public sealed class ListTenantUsersHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    IIdentityService identityService,
    ITierLimitChecker tierLimitChecker,
    IClock clock)
    : IRequestHandler<ListTenantUsersQuery, Result<TenantUsersResponse>>
{
    public async Task<Result<TenantUsersResponse>> Handle(
        ListTenantUsersQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.UsersRead, cancellationToken);

        var now = clock.UtcNowOffset;

        // KAN-91. THE ROSTER STARTS FROM THE MEMBERSHIPS, and getting this wrong produced a defect a
        // user hit within the hour: somebody invited from another workspace was refused as "already a
        // member" while being INVISIBLE on this screen. The cause was that this list asked Identity
        // for the tenant_id CLAIM, and a claim holds exactly one workspace — the FIRST one that person
        // ever joined. Their membership of this one could not be expressed there at all.
        //
        // The claim is still unioned in, for an account the B44 backfill could not map (one with a
        // claim and no role). There are none today; without it, such a person would vanish from the
        // screen that manages them.
        var access = await db.TenantUsers.ToListAsync(cancellationToken);
        var accessByUser = access.ToDictionary(a => a.UserId, StringComparer.Ordinal);

        var claimed = await identityService.GetTenantUserIdsAsync(tenantContext.TenantId.ToString());

        var memberIds = access.Select(a => a.UserId)
            .Union(claimed, StringComparer.Ordinal)
            .ToList();

        var summaries = await identityService.GetUserSummariesAsync(memberIds);

        var inviterEmails = await ResolveInviterEmailsAsync(access);

        var users = summaries
            .Select(s =>
            {
                accessByUser.TryGetValue(s.UserId, out var row);
                return new TenantUserDto(
                    s.UserId,
                    s.Email,
                    s.FirstName,
                    s.LastName,
                    // KAN-91. THE ROLE COMES FROM THE MEMBERSHIP, and Identity's is only the fallback
                    // for an account with no row here. Showing Identity's would show what the person
                    // is somewhere ELSE the moment they belong to two workspaces.
                    row?.Role ?? s.Role,
                    // No access row means nobody ever switched this person off — the founder's case.
                    row?.IsActive ?? true,
                    s.EmailConfirmed,
                    row?.CreatedAt ?? default,
                    row?.DeactivatedAt,
                    row?.InvitedBy is { } inviter && inviterEmails.TryGetValue(inviter, out var e) ? e : null);
            })
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invitationRows = await db.Invitations
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var invitations = invitationRows
            .Select(i => InvitationMapper.ToDto(
                i, now, inviterEmails.TryGetValue(i.InvitedBy, out var e) ? e : null))
            .ToList();

        var seats = await tierLimitChecker.GetSeatUsageAsync(cancellationToken);

        return Result<TenantUsersResponse>.Success(new TenantUsersResponse(
            users,
            invitations,
            new SeatUsageDto(seats.Used, seats.ActiveUsers, seats.PendingInvitations, seats.Limit, seats.HasRoom)));
    }

    /// <summary>
    /// Inviter id → email, resolved once for the whole page rather than per row: the same admin
    /// usually invited everybody, so this is one or two lookups, not one per person.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveInviterEmailsAsync(IEnumerable<TenantUser> access)
    {
        var ids = access
            .Select(a => a.InvitedBy)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var email = await identityService.FindEmailByUserIdAsync(id);
            if (email is not null) result[id] = email;
        }

        return result;
    }
}
