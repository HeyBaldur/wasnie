using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Features.Profile;

/// <summary>
/// Whether this login's identity belongs to an administrator rather than to the person using it.
///
/// ★★ THE DISCRIMINATOR IS THE PAYEE LINK, NOT THE ROLE. An account an administrator attached to a
/// payee record (<c>Payee.UserId</c>, set on purpose — never matched on email) is an account whose
/// name is the name on a payslip and whose address is how the workspace reaches them. Letting that
/// person rename themselves puts a second version of their name in the product, next to the one the
/// administrator maintains, and there is no rule that says which is right afterwards.
///
/// ★★ WHY NOT "is this a Rep". Because the role is a proxy for the real thing and gets it wrong in
/// both directions: a Manager who is also paid has exactly the same problem, and an administrator
/// with no payee record has none. The link is the fact; the role is a correlation.
///
/// ★★ DERIVED, NEVER STORED. There is no "self-managed" flag to set, so nothing can fall out of step
/// with reality: linking somebody to a payee makes their identity administered in the same instant,
/// and unlinking hands it back. A flag would need remembering at both ends (§B5).
///
/// ★ NOT LINKED MEANS SELF-MANAGED, and that is the safe default rather than a gap: the accounts this
/// returns false for are the ones nobody else maintains a name for. An unresolvable user id is the
/// same case and returns false — this decides who may edit their OWN profile, so the conservative
/// answer is the one that leaves a person able to fill in their own name.
///
/// ★ THE TENANT FILTER ON <see cref="IApplicationDbContext"/> STILL APPLIES, so the question is
/// always "administered IN THIS WORKSPACE". Somebody who is a payee elsewhere and an administrator
/// here manages their own profile here, which is the right answer.
/// </summary>
public static class AdministeredIdentity
{
    public static async Task<bool> IsAdministeredAsync(
        IApplicationDbContext db, string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return await db.Payees.AnyAsync(p => p.UserId == userId, cancellationToken);
    }
}
