namespace Wasnie.Application.Authorization;

/// <summary>
/// WHICH payees the asking user may see, as a value rather than a boolean.
///
/// Two shapes, because two questions need answering and a boolean only answers the first:
///   • "may I read payee X" → <see cref="Allows"/>, the per-resource check (BOLA/IDOR).
///   • "which payees go in this LIST" → <see cref="All"/> + <see cref="PayeeIds"/>, because an endpoint
///     like terminated-with-balance takes no payee id at all. A per-resource guard cannot protect a
///     list; it has to be filtered, and filtering needs the set.
///
/// ★ THE EMPTY SET IS THE DEFAULT. <see cref="None"/> is what every unresolved case produces — no
/// user id on the token, an unrecognised role, a Rep whose payee is not linked. Access is something
/// this type GRANTS, never something it forgets to take away.
/// </summary>
public sealed record PayeeVisibility(bool All, IReadOnlySet<Guid> PayeeIds)
{
    /// <summary>Supervisory roles: every payee in the tenant (the tenant filter still applies).</summary>
    public static readonly PayeeVisibility Everything = new(true, new HashSet<Guid>());

    /// <summary>Nothing. The fail-closed state, and the one every error path lands on.</summary>
    public static readonly PayeeVisibility None = new(false, new HashSet<Guid>());

    public static PayeeVisibility Of(params Guid[] payeeIds) =>
        new(false, new HashSet<Guid>(payeeIds));

    public bool Allows(Guid payeeId) => All || PayeeIds.Contains(payeeId);

    /// <summary>True when this visibility can be expressed as "no filter" on a list query.</summary>
    public bool IsUnrestricted => All;
}
