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
    /// Every role an administrator may grant, in the order the picker shows them — most authority
    /// first, which is how the same list reads in the permission map.
    ///
    /// ★ TENANT ADMIN IS IN IT. Handing over administration is a legitimate thing to need — somebody
    /// has to be able to make a second admin before the first one leaves the company — and the guard
    /// against emptying the role is the USER_LAST_ADMIN refusal, applied when the LAST one would go,
    /// not a rule against ever creating another.
    /// </summary>
    public static readonly IReadOnlyList<string> Assignable =
        [TenantAdmin, CompManager, Manager, Rep];

    /// <summary>Whether a role name is one this product actually has. Case-insensitive, as Identity is.</summary>
    public static bool IsAssignable(string? role) =>
        role is not null &&
        Assignable.Any(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The canonical spelling of a role name, so a row never stores "tenantadmin".
    /// Returns null when the name is not a role.
    /// </summary>
    public static string? Canonical(string? role) =>
        role is null
            ? null
            : Assignable.FirstOrDefault(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));
}
