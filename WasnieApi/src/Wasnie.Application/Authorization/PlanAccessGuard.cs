using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;

namespace Wasnie.Application.Authorization;

/// <summary>
/// Answers "may this user read THIS plan" (KAN-93, bug 6).
///
/// The rules, and why each one is what it is:
///
///   • <b><c>Plans.Read</c> → every plan.</b> Running compensation is reading the catalogue;
///     administrators and compensation managers need all of it. The tenant query filter still
///     applies, so "every plan" always means "every plan in THIS workspace".
///
///   • <b><c>Plans.ReadOwn</c> → the plans behind their OWN assignments.</b> Resolved through
///     <c>Payee.UserId</c> — the link an administrator sets on purpose — and then through
///     PlanAssignment. This is the transparency case: a rep checking the arithmetic on their payslip.
///
///   • <b>Neither permission → nothing.</b>
///
/// ★★ ASSIGNMENT STATUS IS DELIBERATELY IGNORED. A rep whose assignment was deactivated last month was
/// still PAID under that plan, and the rules behind a payment they can still see in their ledger must
/// stay readable — otherwise unassigning somebody silently deletes their ability to check money they
/// have already received, which is the opposite of what this feature is for. Only ever having been
/// assigned is the question, not being assigned today.
///
/// ★★ EVERY UNRESOLVED CASE DENIES. No role, no user id, no linked payee: all return false before any
/// plan is looked at. In particular a Rep whose account is not linked to a payee resolves to "no
/// plans" rather than to "no restriction" — the query is anchored on their payee id, so an
/// unresolvable identity produces an empty set instead of a wildcard. That is the single most
/// important property of this file; read it that way when changing it.
///
/// ★ IT IS NOT THE TENANT GUARD. Every query runs through <see cref="IApplicationDbContext"/>, whose
/// global filter already scopes plans and assignments to the current tenant. This class narrows WITHIN
/// a tenant and never widens across one.
/// </summary>
public sealed class PlanAccessGuard(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IAuthorizationService authorizationService)
    : IPlanAccessGuard
{
    public async Task<bool> CanReadAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        if (await authorizationService.HasAsync(Permission.PlansRead, cancellationToken))
            return true;

        if (!await authorizationService.HasAsync(Permission.PlansReadOwn, cancellationToken))
            return false;

        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        // ★ ONE QUERY, NOT TWO. Resolving the payee first and then its assignments would be a second
        // round trip and a second chance to forget the tenant filter; joined, an unlinked account
        // simply matches nothing.
        return await db.PlanAssignments
            .AnyAsync(
                a => a.PlanId == planId
                     && db.Payees.Any(p => p.Id == a.PayeeId && p.UserId == userId),
                cancellationToken);
    }
}
