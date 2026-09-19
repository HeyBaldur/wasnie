using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Application.Features.Users.Handlers;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-93, Bug 3: a workspace cannot be left with nobody who can administer it.
///
/// ★★ THESE TESTS EXIST BECAUSE THE TICKET SAID THE RULES WERE MISSING AND THEY WERE NOT. KAN-93 was
/// filed as "an admin can demote themselves and lose the account forever"; the backend has refused
/// that since KAN-32 (<c>CannotActOnSelf</c>) and has counted admins since then too
/// (<c>LastAdminGuard</c>). What was actually broken was the screen, which offered the actions anyway
/// — so the only way to learn the rule was to press a button and read a toast.
///
/// ★★ SO THE FIX IS IN THE TEMPLATE AND THE PROOF IS HERE. A guard nobody exercises is a guard that
/// gets refactored away: with the menu items now hidden, no ordinary use of the product reaches these
/// branches any more, and without a test the next person to touch <c>ChangeUserRoleHandler</c> has
/// nothing telling them the refusals are load-bearing. That is the whole reason this file was added
/// for a ticket whose backend did not change.
///
/// ★ IN-MEMORY, AND THAT IS ENOUGH HERE. Every rule under test is expressed in LINQ over memberships
/// and evaluated the same way by the provider; nothing depends on SQL semantics.
/// </summary>
public sealed class UserAccessInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private const string AdminUser = "user-admin";
    private const string OtherAdminUser = "user-admin-2";
    private const string RepUser = "user-rep";

    private sealed record Harness(
        ApplicationDbContext Db,
        ChangeUserRoleHandler ChangeRole,
        DeactivateUserHandler Deactivate);

    /// <summary>
    /// One workspace, with whichever memberships the test names. The acting user is always
    /// <see cref="AdminUser"/>, because every rule here is about what an administrator may do.
    /// </summary>
    private static Harness Seed(string dbName, params (string UserId, string Role)[] members)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        foreach (var (userId, role) in members)
            db.TenantUsers.Add(TenantUser.Create(Guid.NewGuid(), tenantId, userId, role, null, null, Now));

        db.SaveChanges();

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(AdminUser);
        currentUser.Email.Returns("admin@acme.com");

        var identity = Substitute.For<IIdentityService>();
        identity.FindEmailByUserIdAsync(Arg.Any<string>()).Returns("someone@acme.com");

        var audit = Substitute.For<IAuditService>();

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        return new Harness(
            db,
            new ChangeUserRoleHandler(db, auth, tenantCtx, currentUser, identity, audit),
            new DeactivateUserHandler(db, auth, tenantCtx, currentUser, identity, audit, clock));
    }

    private static async Task<string> RefusalCodeOf(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<DomainCodedException>(() => act());
        return ex.Code;
    }

    /// <summary>
    /// ★★ THE ONE THE TICKET IS ABOUT. An administrator demoting themselves is refused whether or not
    /// anybody else is an admin — it is not the last-admin rule wearing a different hat, and the
    /// second member in this workspace is there to prove exactly that.
    /// </summary>
    [Fact]
    public async Task An_admin_cannot_change_their_own_role()
    {
        var h = Seed(nameof(An_admin_cannot_change_their_own_role),
            (AdminUser, Roles.TenantAdmin), (OtherAdminUser, Roles.TenantAdmin));

        var code = await RefusalCodeOf(() =>
            h.ChangeRole.Handle(new ChangeUserRoleCommand(AdminUser, Roles.Rep), default));

        code.Should().Be(InvitationRefusal.CannotActOnSelf);
        h.Db.TenantUsers.Single(u => u.UserId == AdminUser).Role.Should().Be(Roles.TenantAdmin);
    }

    [Fact]
    public async Task An_admin_cannot_deactivate_themselves()
    {
        var h = Seed(nameof(An_admin_cannot_deactivate_themselves),
            (AdminUser, Roles.TenantAdmin), (OtherAdminUser, Roles.TenantAdmin));

        var code = await RefusalCodeOf(() =>
            h.Deactivate.Handle(new DeactivateUserCommand(AdminUser), default));

        code.Should().Be(InvitationRefusal.CannotActOnSelf);
    }

    /// <summary>
    /// ★★ THE INVARIANT IS "ALWAYS AT LEAST ONE ADMIN", and this is the case that makes it bite:
    /// demoting somebody ELSE, which self-refusal does not cover. A second administrator exists here
    /// only as a Rep, so the target is genuinely the last one.
    /// </summary>
    [Fact]
    public async Task The_last_admin_cannot_be_demoted_by_another_admin()
    {
        var h = Seed(nameof(The_last_admin_cannot_be_demoted_by_another_admin),
            (AdminUser, Roles.CompManager), (OtherAdminUser, Roles.TenantAdmin));

        var code = await RefusalCodeOf(() =>
            h.ChangeRole.Handle(new ChangeUserRoleCommand(OtherAdminUser, Roles.Rep), default));

        code.Should().Be(InvitationRefusal.LastAdmin);
        h.Db.TenantUsers.Single(u => u.UserId == OtherAdminUser).Role.Should().Be(Roles.TenantAdmin);
    }

    /// <summary>
    /// ★ AND IT COUNTS ACTIVE ADMINS, NOT ADMINS. A workspace whose only other administrator was
    /// switched off has one administrator, whatever the roles column says — otherwise deactivating
    /// somebody and then demoting the survivor would empty the role in two legal-looking steps.
    /// </summary>
    [Fact]
    public async Task A_deactivated_admin_does_not_count_towards_the_invariant()
    {
        var h = Seed(nameof(A_deactivated_admin_does_not_count_towards_the_invariant),
            (AdminUser, Roles.CompManager),
            (OtherAdminUser, Roles.TenantAdmin),
            (RepUser, Roles.TenantAdmin));

        var sleeping = h.Db.TenantUsers.Single(u => u.UserId == RepUser);
        sleeping.Deactivate(AdminUser, Now);
        await h.Db.SaveChangesAsync();

        var code = await RefusalCodeOf(() =>
            h.ChangeRole.Handle(new ChangeUserRoleCommand(OtherAdminUser, Roles.Rep), default));

        code.Should().Be(InvitationRefusal.LastAdmin);
    }

    /// <summary>
    /// ★★ THE RULE MUST NOT SWALLOW THE LEGITIMATE CASE. Handing administration to somebody else is
    /// how an admin leaves the company without stranding the workspace, and the ticket asks for it
    /// explicitly. A guard that blocked this would be worse than the bug it fixed.
    /// </summary>
    [Fact]
    public async Task An_admin_can_promote_somebody_else_to_admin()
    {
        var h = Seed(nameof(An_admin_can_promote_somebody_else_to_admin),
            (AdminUser, Roles.TenantAdmin), (RepUser, Roles.Rep));

        var result = await h.ChangeRole.Handle(
            new ChangeUserRoleCommand(RepUser, Roles.TenantAdmin), default);

        result.IsSuccess.Should().BeTrue();
        h.Db.TenantUsers.Single(u => u.UserId == RepUser).Role.Should().Be(Roles.TenantAdmin);
    }

    /// <summary>
    /// ★ AND THEN THE FIRST ONE CAN STEP DOWN — by somebody else's hand, which is the only way the
    /// self-refusal allows. This is the full handover the ticket describes, in two moves.
    /// </summary>
    [Fact]
    public async Task Once_a_second_admin_exists_the_first_can_be_demoted()
    {
        var h = Seed(nameof(Once_a_second_admin_exists_the_first_can_be_demoted),
            (AdminUser, Roles.TenantAdmin), (OtherAdminUser, Roles.TenantAdmin));

        var result = await h.ChangeRole.Handle(
            new ChangeUserRoleCommand(OtherAdminUser, Roles.Rep), default);

        result.IsSuccess.Should().BeTrue();
        h.Db.TenantUsers.Count(u => u.Role == Roles.TenantAdmin).Should().Be(1);
    }

    /// <summary>
    /// ★★ ROLE SIMPLIFICATION (KAN-92/KAN-99): HIDING THE ROLE IN THE PICKER IS NOT ENOUGH. Anybody with
    /// Users.Manage and a terminal can still send "CompManager"; the handler is what refuses it.
    /// </summary>
    [Theory]
    [InlineData(Roles.CompManager)]
    [InlineData(Roles.Manager)]
    [InlineData("compmanager")]
    public async Task Nobody_can_be_moved_into_a_hidden_role(string hidden)
    {
        var h = Seed(nameof(Nobody_can_be_moved_into_a_hidden_role) + hidden,
            (AdminUser, Roles.TenantAdmin), (RepUser, Roles.Rep));

        var code = await RefusalCodeOf(() =>
            h.ChangeRole.Handle(new ChangeUserRoleCommand(RepUser, hidden), default));

        code.Should().Be(InvitationRefusal.RoleNotAssignable);
        h.Db.TenantUsers.Single(u => u.UserId == RepUser).Role.Should().Be(Roles.Rep);
    }

    /// <summary>
    /// ★ AND THE REFUSAL DOES NOT DEPEND ON WHAT THE PERSON HOLDS TODAY. "Change to the role you already
    /// have" is normally a quiet no-op; for a hidden role it is refused like any other request for it.
    /// </summary>
    [Fact]
    public async Task Re_asserting_a_hidden_role_is_refused_too()
    {
        var h = Seed(nameof(Re_asserting_a_hidden_role_is_refused_too),
            (AdminUser, Roles.TenantAdmin), (RepUser, Roles.Manager));

        var code = await RefusalCodeOf(() =>
            h.ChangeRole.Handle(new ChangeUserRoleCommand(RepUser, Roles.Manager), default));

        code.Should().Be(InvitationRefusal.RoleNotAssignable);
    }

    /// <summary>
    /// ★★ THE WAY OUT STAYS OPEN (§D4). Somebody who already holds a hidden role must be movable to an
    /// assignable one — otherwise the simplification would freeze them where they are.
    /// </summary>
    [Fact]
    public async Task Somebody_in_a_hidden_role_can_be_moved_to_an_assignable_one()
    {
        var h = Seed(nameof(Somebody_in_a_hidden_role_can_be_moved_to_an_assignable_one),
            (AdminUser, Roles.TenantAdmin), (RepUser, Roles.CompManager));

        var result = await h.ChangeRole.Handle(new ChangeUserRoleCommand(RepUser, Roles.Rep), default);

        result.IsSuccess.Should().BeTrue();
        h.Db.TenantUsers.Single(u => u.UserId == RepUser).Role.Should().Be(Roles.Rep);
    }

    /// <summary>An unknown role keeps its own, different answer: "does not exist" is true for it.</summary>
    [Fact]
    public async Task An_unknown_role_is_still_unknown_not_hidden()
    {
        var h = Seed(nameof(An_unknown_role_is_still_unknown_not_hidden),
            (AdminUser, Roles.TenantAdmin), (RepUser, Roles.Rep));

        var code = await RefusalCodeOf(() =>
            h.ChangeRole.Handle(new ChangeUserRoleCommand(RepUser, "Auditor"), default));

        code.Should().Be(InvitationRefusal.RoleUnknown);
    }
}
