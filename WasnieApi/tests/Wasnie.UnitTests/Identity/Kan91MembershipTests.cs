using FluentAssertions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Identity;

namespace Wasnie.UnitTests.Identity;

/// <summary>
/// KAN-91 — one account, several workspaces, a role in each.
///
/// ★★ THE INVARIANT THESE PROTECT is that a role belongs to a MEMBERSHIP and never to the account.
/// Identity's own <c>AspNetUserRoles</c> has no tenant column, so the day somebody "simplifies" this
/// back into a global role, a person who administers one department would administer every workspace
/// they belong to. These are the tests that would fail first.
/// </summary>
public sealed class Kan91MembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static TenantUser Membership(Guid tenantId, string role) =>
        TenantUser.Create(Guid.NewGuid(), tenantId, "user-1", role, null, null, Now);

    [Fact]
    public void The_same_account_holds_a_different_role_in_each_workspace()
    {
        var departmentA = Membership(Guid.NewGuid(), Roles.TenantAdmin);
        var departmentB = Membership(Guid.NewGuid(), Roles.Rep);

        departmentA.Role.Should().Be(Roles.TenantAdmin);
        departmentB.Role.Should().Be(Roles.Rep);

        // The point of the whole ticket: administering one does not administer the other.
        departmentA.Role.Should().NotBe(departmentB.Role);
        departmentA.UserId.Should().Be(departmentB.UserId, "it is one human being with one account");
    }

    [Fact]
    public void Changing_a_role_touches_only_that_membership()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var inA = Membership(tenantA, Roles.TenantAdmin);
        var inB = Membership(tenantB, Roles.Rep);

        inB.ChangeRole(Roles.CompManager);

        inB.Role.Should().Be(Roles.CompManager);
        inA.Role.Should().Be(Roles.TenantAdmin, "the other workspace did not promote anybody");
    }

    [Fact]
    public void A_membership_cannot_exist_without_a_role()
    {
        var blank = () => TenantUser.Create(
            Guid.NewGuid(), Guid.NewGuid(), "user-1", "  ", null, null, Now);

        var cleared = () => Membership(Guid.NewGuid(), Roles.Rep).ChangeRole("");

        blank.Should().Throw<ArgumentException>();
        cleared.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_role_is_stored_in_its_canonical_spelling()
    {
        Membership(Guid.NewGuid(), "  CompManager  ").Role.Should().Be("CompManager");
    }

    /// <summary>
    /// ★ DEACTIVATION IS PER WORKSPACE TOO. Somebody switched off in one company must keep getting
    /// into the other; the sign-in path refuses only when EVERY membership is closed.
    /// </summary>
    [Fact]
    public void Deactivating_one_membership_leaves_the_other_open()
    {
        var inA = Membership(Guid.NewGuid(), Roles.TenantAdmin);
        var inB = Membership(Guid.NewGuid(), Roles.Rep);

        inA.Deactivate("admin", Now);

        inA.IsActive.Should().BeFalse();
        inB.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Reactivating_still_never_erases_the_history()
    {
        var m = Membership(Guid.NewGuid(), Roles.Rep);
        m.Deactivate("admin", Now);
        m.Reactivate("admin", Now.AddDays(1));

        m.IsActive.Should().BeTrue();
        m.DeactivatedAt.Should().Be(Now, "§B6 — nothing goes back to null");
        m.ReactivatedAt.Should().Be(Now.AddDays(1));
    }
}
