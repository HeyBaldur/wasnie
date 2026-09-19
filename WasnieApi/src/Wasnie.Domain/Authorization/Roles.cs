namespace Wasnie.Domain.Authorization;

/// <summary>
/// THE ROLE NAMES AS IDENTITY HOLDS THEM, AND THE ONE LIST OF WHAT MAY BE HANDED OUT (KAN-32).
///
/// ★★ IT EXISTS BECAUSE THE NAMES WERE LOOSE STRINGS IN THREE PLACES — the seeder, the tenant
/// registration and the permission map — with nothing tying them together. Inviting somebody adds a
/// FOURTH place where a typo would produce a user in a role that grants nothing and looks fine on
/// screen, which is the quietest possible failure for an authorisation system.
///
/// ★ <see cref="Role"/> IS THE ENUM AND STAYS THE ENUM. This is deliberately not a replacement for
/// it: Identity stores role membership as text, so the boundary needs the text, and having the two
/// side by side is better than converting an enum to a string at every call site with its own casing.
/// </summary>
public static class Roles
{
    public const string TenantAdmin = "TenantAdmin";
    public const string CompManager = "CompManager";
    public const string Manager = "Manager";
    public const string Rep = "Rep";

    /// <summary>
    /// Every role this product HAS — the ones a membership row may hold and the permission map
    /// describes. Most authority first, which is how the permission map reads.
    ///
    /// ★ NOT THE SAME LIST AS <see cref="Assignable"/>, and the difference is the point. A role can
    /// exist without being grantable: CompManager and Manager keep their permissions and their
    /// meaning, so a membership that already holds one keeps working, but nobody is handed one today.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
        [TenantAdmin, CompManager, Manager, Rep];

    /// <summary>
    /// The roles an administrator may GRANT — by invitation or by changing somebody's role — in the
    /// order the picker shows them.
    ///
    /// ★★ THE ONE PLACE TO REACTIVATE A ROLE. CompManager and Manager were hidden, not deleted: the
    /// mid-market customer of today needs "who administers pay" and "who is paid", and Manager would
    /// need a team hierarchy nobody has built yet. Bringing either back — or adding a new role — is
    /// adding it here. The screens do not keep their own copy: they read this list through
    /// <c>GET /api/users/roles</c>, so a picker cannot offer what the server would refuse.
    ///
    /// ★ TENANT ADMIN IS IN IT. Handing over administration is a legitimate thing to need — somebody
    /// has to be able to make a second admin before the first one leaves the company — and the guard
    /// against emptying the role is the USER_LAST_ADMIN refusal, applied when the LAST one would go,
    /// not a rule against ever creating another.
    /// </summary>
    public static readonly IReadOnlyList<string> Assignable =
        [TenantAdmin, Rep];

    /// <summary>Whether a role name is one this product has at all. Case-insensitive, as Identity is.</summary>
    public static bool IsKnown(string? role) => Canonical(role) is not null;

    /// <summary>Whether a role may be granted today. Case-insensitive, as Identity is.</summary>
    public static bool IsAssignable(string? role) =>
        role is not null &&
        Assignable.Any(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The canonical spelling of a role name, so a row never stores "tenantadmin".
    /// Returns null when the name is not a role. Resolves against <see cref="All"/>, not
    /// <see cref="Assignable"/>: spelling a hidden role correctly is not granting it.
    /// </summary>
    public static string? Canonical(string? role) =>
        role is null
            ? null
            : All.FirstOrDefault(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));
}
