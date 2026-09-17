using FluentAssertions;
using Wasnie.Domain.Authorization;
using Wasnie.Application.Authorization;
using Wasnie.Domain.Identity;

namespace Wasnie.UnitTests.Identity;

/// <summary>
/// KAN-32 — the invariants of an invitation and of somebody's access.
///
/// ★★ THE TWO THAT MATTER MOST ARE THE DERIVED ONES. Invitation status and TenantUser.IsActive are
/// computed rather than stored precisely so they cannot drift, and these are the tests that would
/// fail the day somebody "optimises" either into a column.
/// </summary>
public sealed class Kan32InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    private static Invitation Make(DateTimeOffset? expires = null) =>
        Invitation.Create(
            Guid.NewGuid(), Tenant, "  New.Person@Example.COM ", Roles.CompManager,
            "hash", "admin-user", expires ?? Now.AddDays(7), Now);

    // ── The derived status ────────────────────────────────────────────────────

    [Fact]
    public void A_fresh_invitation_is_pending()
    {
        Make().StatusAt(Now).Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public void It_becomes_expired_the_moment_the_deadline_passes_with_nothing_stored()
    {
        var invitation = Make(Now.AddDays(7));

        // ★ NOTHING RAN AT THE DEADLINE. No job, no write — only the clock moved, and the row now
        // reads Expired. That is the whole argument for deriving it (§B5).
        invitation.StatusAt(Now.AddDays(7)).Should().Be(InvitationStatus.Expired);
        invitation.StatusAt(Now.AddDays(8)).Should().Be(InvitationStatus.Expired);
    }

    [Fact]
    public void Accepting_wins_over_expiry_in_the_status()
    {
        var invitation = Make();
        invitation.Accept("new-user", Now.AddDays(1));

        // Long past the deadline it still reads Accepted: what happened outranks what did not.
        invitation.StatusAt(Now.AddDays(30)).Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public void Revoking_is_visible_before_the_deadline_and_after_it()
    {
        var invitation = Make();
        invitation.Revoke("admin-user", Now.AddDays(1));

        invitation.StatusAt(Now.AddDays(2)).Should().Be(InvitationStatus.Revoked);
        invitation.StatusAt(Now.AddDays(30)).Should().Be(InvitationStatus.Revoked);
    }

    // ── Single use ────────────────────────────────────────────────────────────

    [Fact]
    public void An_accepted_invitation_cannot_be_accepted_again()
    {
        var invitation = Make();
        invitation.Accept("new-user", Now.AddDays(1));

        var second = () => invitation.Accept("someone-else", Now.AddDays(2));

        second.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_expired_invitation_cannot_be_accepted()
    {
        var invitation = Make(Now.AddDays(1));

        var late = () => invitation.Accept("new-user", Now.AddDays(2));

        late.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_revoked_invitation_cannot_be_accepted_or_resent()
    {
        var invitation = Make();
        invitation.Revoke("admin-user", Now.AddDays(1));

        var accept = () => invitation.Accept("new-user", Now.AddDays(2));
        var resend = () => invitation.Resend("hash2", Now.AddDays(9), Now.AddDays(2));

        accept.Should().Throw<InvalidOperationException>();
        resend.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resending_replaces_the_token_so_the_old_link_stops_working()
    {
        var invitation = Make();
        var originalHash = invitation.TokenHash;

        invitation.Resend("a-new-hash", Now.AddDays(8), Now.AddDays(1));

        invitation.TokenHash.Should().NotBe(originalHash);
        invitation.TokenHash.Should().Be("a-new-hash");
        invitation.ResendCount.Should().Be(1);
        invitation.ExpiresAt.Should().Be(Now.AddDays(8));
    }

    // ── Normalisation ─────────────────────────────────────────────────────────

    [Fact]
    public void The_email_is_folded_to_lower_case_so_it_matches_AND_reads_properly_on_screen()
    {
        Make().Email.Should().Be("new.person@example.com");
    }

    [Fact]
    public void An_invitation_must_have_a_tenant_a_role_and_a_future_deadline()
    {
        var noTenant = () => Invitation.Create(
            Guid.NewGuid(), Guid.Empty, "a@b.com", Roles.Rep, "h", "admin", Now.AddDays(1), Now);
        var noRole = () => Invitation.Create(
            Guid.NewGuid(), Tenant, "a@b.com", "  ", "h", "admin", Now.AddDays(1), Now);
        var alreadyDead = () => Invitation.Create(
            Guid.NewGuid(), Tenant, "a@b.com", Roles.Rep, "h", "admin", Now, Now);

        noTenant.Should().Throw<ArgumentException>();
        noRole.Should().Throw<ArgumentException>();
        alreadyDead.Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// KAN-32 — access, and the §B6 promise that nothing goes back to null.
/// </summary>
public sealed class Kan32TenantUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static TenantUser Make() =>
        TenantUser.Create(Guid.NewGuid(), Guid.NewGuid(), "user-1", Roles.TenantAdmin, null, null, Now);

    [Fact]
    public void A_new_access_row_is_active_and_has_no_history()
    {
        var access = Make();

        access.IsActive.Should().BeTrue();
        access.DeactivatedAt.Should().BeNull();
        access.ReactivatedAt.Should().BeNull();
    }

    [Fact]
    public void Deactivating_closes_access_and_records_who_did_it()
    {
        var access = Make();

        access.Deactivate("admin-user", Now.AddDays(1));

        access.IsActive.Should().BeFalse();
        access.DeactivatedAt.Should().Be(Now.AddDays(1));
        access.DeactivatedBy.Should().Be("admin-user");
    }

    /// <summary>
    /// ★★ THE ONE THIS MODULE EXISTS TO GET RIGHT. <c>Payee.Activate</c> sets
    /// <c>DeactivatedAt = null</c> (<c>Payee.cs:266</c>) and erases the fact that the person was ever
    /// switched off; §B6 names that as a defect and the ticket forbids repeating it. If somebody ever
    /// "tidies" Reactivate into clearing the timestamp, this is what stops them.
    /// </summary>
    [Fact]
    public void Reactivating_reopens_access_WITHOUT_erasing_that_it_was_ever_closed()
    {
        var access = Make();
        access.Deactivate("admin-user", Now.AddDays(1));

        access.Reactivate("admin-user", Now.AddDays(5));

        access.IsActive.Should().BeTrue();
        access.DeactivatedAt.Should().Be(Now.AddDays(1), "the history must survive reactivation");
        access.ReactivatedAt.Should().Be(Now.AddDays(5));
    }

    [Fact]
    public void A_second_cycle_still_resolves_to_the_later_timestamp()
    {
        var access = Make();
        access.Deactivate("a", Now.AddDays(1));
        access.Reactivate("a", Now.AddDays(2));
        access.Deactivate("a", Now.AddDays(3));

        access.IsActive.Should().BeFalse("the later of the two timestamps decides");
        access.ReactivatedAt.Should().Be(Now.AddDays(2), "and neither one is cleared");
    }

    [Fact]
    public void Deactivating_twice_keeps_the_first_timestamp()
    {
        var access = Make();
        access.Deactivate("a", Now.AddDays(1));
        access.Deactivate("b", Now.AddDays(4));

        access.DeactivatedAt.Should().Be(Now.AddDays(1));
        access.DeactivatedBy.Should().Be("a");
    }
}

/// <summary>KAN-32 — the role names, which used to be loose strings in three places.</summary>
public sealed class Kan32RolesTests
{
    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("tenantadmin")]
    [InlineData("  CompManager  ")]
    [InlineData("REP")]
    public void Known_roles_are_assignable_whatever_the_casing(string role)
    {
        Roles.IsAssignable(role).Should().BeTrue();
    }

    [Theory]
    [InlineData("Auditor")]
    [InlineData("Admin")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_refused(string? role)
    {
        Roles.IsAssignable(role).Should().BeFalse();
    }

    [Fact]
    public void Canonical_fixes_the_spelling_so_a_row_never_stores_tenantadmin()
    {
        Roles.Canonical(" tenantADMIN ").Should().Be("TenantAdmin");
        Roles.Canonical("nonsense").Should().BeNull();
    }

    /// <summary>
    /// The Auditor role of KAN-33 does not exist yet, and this test says so out loud: when that
    /// ticket lands it will fail, which is the reminder to add it to the picker.
    /// </summary>
    [Fact]
    public void There_are_exactly_four_roles_today()
    {
        Roles.Assignable.Should().HaveCount(4);
        Roles.Assignable.Should().NotContain("Auditor");
    }
}

/// <summary>KAN-32 — who may administer users.</summary>
public sealed class Kan32PermissionTests
{
    [Fact]
    public void Only_a_tenant_admin_may_manage_users()
    {
        RolePermissions.HasPermission(Roles.TenantAdmin, Permission.UsersManage).Should().BeTrue();

        RolePermissions.HasPermission(Roles.CompManager, Permission.UsersManage).Should().BeFalse();
        RolePermissions.HasPermission(Roles.Manager, Permission.UsersManage).Should().BeFalse();
        RolePermissions.HasPermission(Roles.Rep, Permission.UsersManage).Should().BeFalse();
    }

    [Fact]
    public void A_comp_manager_may_read_the_roster_without_being_able_to_change_it()
    {
        RolePermissions.HasPermission(Roles.CompManager, Permission.UsersRead).Should().BeTrue();
        RolePermissions.HasPermission(Roles.CompManager, Permission.UsersManage).Should().BeFalse();
    }

    /// <summary>The BDD fail-safe from the ticket: a Manager or Rep gets 403, so neither holds either.</summary>
    [Fact]
    public void A_manager_or_rep_sees_nothing_of_user_administration()
    {
        foreach (var role in new[] { Roles.Manager, Roles.Rep })
        {
            RolePermissions.HasPermission(role, Permission.UsersRead).Should().BeFalse();
            RolePermissions.HasPermission(role, Permission.UsersManage).Should().BeFalse();
        }
    }
}
