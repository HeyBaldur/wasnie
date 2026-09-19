using Wasnie.Domain.Common;

namespace Wasnie.Domain.Identity;

/// <summary>
/// ONE PERSON'S MEMBERSHIP OF ONE WORKSPACE: who let them in, what they may do THERE, and whether
/// their access is currently open.
///
/// ★★ IT BECAME THE SOURCE OF TRUTH IN KAN-91, AND IT DID NOT START THAT WAY. KAN-32 built it as a
/// record BESIDE Identity — the tenant was the <c>tenant_id</c> claim, the role was a global Identity
/// role, and this row only remembered deactivation. That model cannot express what a real company
/// needs: the same person administering two departments. Identity gives a user ONE claim and roles
/// with NO tenant, so a person in two workspaces would carry the union of both roles in both places.
///
/// ★★ SO THE MEMBERSHIP MOVED HERE AND THE TOKEN IS ISSUED FROM IT. Sign-in resolves which membership
/// the session is for and writes THAT tenant and THAT role into the JWT. Everything downstream —
/// <c>ITenantContext</c>, <c>AuthorizationService</c>, every query filter — already read the token, so
/// none of them changed. The claim and the Identity roles still exist and are no longer consulted for
/// authorisation; where they disagree with this row, this row is right.
///
/// ★★ NOTHING EVER GOES BACK TO NULL. <c>Payee.Activate</c> sets <c>DeactivatedAt = null</c>
/// (<c>Payee.cs:266</c>) and erases the fact that the person was ever switched off; §B6 names that as a
/// defect and the ticket for this module says it in so many words. So both timestamps are written once
/// per event and never cleared, and the CURRENT state is derived from which of the two is later.
///
/// ★ THERE IS NO IsActive COLUMN, for the same reason there is no stored invitation status: a flag
/// beside the timestamps that produced it is a flag that can drift (§B5). <see cref="IsActive"/> is
/// computed, and <see cref="ActiveSpec"/> is the one spelling of that rule for queries, so the database
/// and the object can never answer differently.
/// </summary>
public sealed class TenantUser : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>ASP.NET Identity's key, a string — matching <c>Payee.UserId</c>.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>
    /// The role this person holds IN THIS WORKSPACE (KAN-91).
    ///
    /// ★★ THE ROLE IS PER MEMBERSHIP, AND THAT IS THE WHOLE POINT. Identity's own
    /// <c>AspNetUserRoles</c> has only <c>UserId</c> and <c>RoleId</c> — no tenant — so a person in
    /// two workspaces would carry the UNION of both roles everywhere. Somebody who administers one
    /// department and merely reads another would administer both. That is an authorisation hole, not
    /// an inconvenience, and it is why the role had to move here.
    ///
    /// ★ IDENTITY'S ROLES ARE NOT DELETED, AND THEY ARE NO LONGER THE TRUTH. They stay because
    /// nothing else has to change at once and because removing them is a separate, irreversible step;
    /// the token is issued from THIS column. If the two ever disagree, this one is right.
    /// </summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>The invitation this access came from. Null for the founder, who predates any invite.</summary>
    public Guid? InvitationId { get; private set; }

    /// <summary>Who sent the invitation, or null when the account created the tenant itself.</summary>
    public string? InvitedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When access was last closed. Written once per deactivation, NEVER cleared.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public string? DeactivatedBy { get; private set; }

    /// <summary>When access was last re-opened. Written once per reactivation, NEVER cleared.</summary>
    public DateTimeOffset? ReactivatedAt { get; private set; }

    public string? ReactivatedBy { get; private set; }

    /// <summary>
    /// Whether this person may sign in right now, derived from the two timestamps.
    ///
    /// Never deactivated → active. Deactivated and never re-opened → not active. Both present → the
    /// later one wins, which is what lets a person be switched off and on again without either fact
    /// being erased.
    /// </summary>
    public bool IsActive =>
        DeactivatedAt is null || (ReactivatedAt is not null && ReactivatedAt > DeactivatedAt);

    /// <summary>
    /// The same rule as <see cref="IsActive"/>, written so EF can translate it to SQL.
    ///
    /// ★ IT EXISTS SO THERE IS EXACTLY ONE RULE. A seat count that filtered with its own hand-written
    /// predicate would be a second definition of "active", and the first time somebody edited one of
    /// the two the count and the screen would stop agreeing.
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<TenantUser, bool>> ActiveSpec =>
        u => u.DeactivatedAt == null || (u.ReactivatedAt != null && u.ReactivatedAt > u.DeactivatedAt);

    private TenantUser() { }

    public static TenantUser Create(
        Guid id,
        Guid tenantId,
        string userId,
        string role,
        Guid? invitationId,
        string? invitedBy,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant user must belong to a tenant.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A tenant user must have a user id.", nameof(userId));
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("A membership must carry a role.", nameof(role));

        return new()
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            Role = role.Trim(),
            InvitationId = invitationId,
            InvitedBy = invitedBy,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Moves this membership to a different role. Affects THIS workspace and no other.
    /// </summary>
    public void ChangeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("A membership must carry a role.", nameof(role));

        Role = role.Trim();
    }

    public void Deactivate(string deactivatedBy, DateTimeOffset now)
    {
        if (!IsActive) return;

        DeactivatedAt = now;
        DeactivatedBy = deactivatedBy;
    }

    public void Reactivate(string reactivatedBy, DateTimeOffset now)
    {
        if (IsActive) return;

        // Deliberately NOT clearing DeactivatedAt — see the class remarks and §B6.
        ReactivatedAt = now;
        ReactivatedBy = reactivatedBy;
    }
}
