using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Users.Commands;
using Wasnie.Application.Features.Users.Handlers;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Identity;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-93, Bug 2: an administrator can attach an existing login to a payee record.
///
/// ★★ THE STATE THIS ENDS WAS PERMANENT. KAN-92 wrote <c>Payee.UserId</c> in exactly one place — the
/// moment somebody accepted an invitation — so anybody whose account already existed when their payee
/// record was created had no path at all. Their personal dashboard said "ask an administrator to link
/// it" and the administrator had nothing to press; re-inviting is refused because the address is
/// already a member. Every test here is the other half of that sentence.
///
/// ★★ THE REFUSALS MATTER MORE THAN THE HAPPY PATH. This link decides whose commissions, balance and
/// ledger somebody may read, so pointing it at the wrong record hands one person another person's pay.
/// Three of the five tests below are about what the handler must NOT do.
/// </summary>
public sealed class LinkUserToPayeeHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private const string AdminUser = "user-admin";
    private const string MemberUser = "user-member";
    private const string StrangerUser = "user-stranger";

    private sealed record Harness(ApplicationDbContext Db, LinkUserToPayeeHandler Handler, Guid TenantId);

    private static Harness Seed(string dbName)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<MediatR.IPublisher>());

        db.TenantUsers.Add(TenantUser.Create(
            Guid.NewGuid(), tenantId, AdminUser, Roles.TenantAdmin, null, null, Now));
        db.TenantUsers.Add(TenantUser.Create(
            Guid.NewGuid(), tenantId, MemberUser, Roles.Rep, null, null, Now));
        db.SaveChanges();

        var auth = Substitute.For<IAuthorizationService>();
        auth.RequireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(AdminUser);
        currentUser.Email.Returns("admin@acme.com");

        var identity = Substitute.For<IIdentityService>();
        identity.FindEmailByUserIdAsync(Arg.Any<string>()).Returns("member@acme.com");

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        return new Harness(
            db,
            new LinkUserToPayeeHandler(
                db, auth, tenantCtx, currentUser, identity, Substitute.For<IAuditService>(), clock),
            tenantId);
    }

    private static Guid AddPayee(Harness h, string code, string? ownerUserId = null)
    {
        var payee = Payee.Create(h.TenantId, $"Payee {code}", code, $"{code}@acme.com".ToLowerInvariant(),
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        if (ownerUserId is not null) payee.LinkToUser(ownerUserId, "test", Now);
        h.Db.Payees.Add(payee);
        h.Db.SaveChanges();
        return payee.Id;
    }

    [Fact]
    public async Task An_existing_user_can_be_attached_to_an_unlinked_payee()
    {
        var h = Seed(nameof(An_existing_user_can_be_attached_to_an_unlinked_payee));
        var payeeId = AddPayee(h, "EMP-1");

        var result = await h.Handler.Handle(new LinkUserToPayeeCommand(MemberUser, payeeId), default);

        result.IsSuccess.Should().BeTrue();
        h.Db.Payees.Single(p => p.Id == payeeId).UserId.Should().Be(MemberUser);
    }

    /// <summary>
    /// ★★ A PAYEE SOMEBODY ELSE OWNS IS REFUSED, NOT RE-POINTED. Moving a link moves who can read that
    /// person's pay; it is an act, not a side effect of filling in a form. The administrator detaches
    /// the other login first, deliberately.
    /// </summary>
    [Fact]
    public async Task A_payee_that_already_belongs_to_somebody_is_refused()
    {
        var h = Seed(nameof(A_payee_that_already_belongs_to_somebody_is_refused));
        var payeeId = AddPayee(h, "EMP-2", StrangerUser);

        var ex = await Assert.ThrowsAsync<DomainCodedException>(() =>
            h.Handler.Handle(new LinkUserToPayeeCommand(MemberUser, payeeId), default));

        ex.Code.Should().Be(InvitationRefusal.PayeeAlreadyLinked);
        h.Db.Payees.Single(p => p.Id == payeeId).UserId.Should().Be(StrangerUser);
    }

    [Fact]
    public async Task A_payee_that_does_not_exist_is_refused()
    {
        var h = Seed(nameof(A_payee_that_does_not_exist_is_refused));

        var ex = await Assert.ThrowsAsync<DomainCodedException>(() =>
            h.Handler.Handle(new LinkUserToPayeeCommand(MemberUser, Guid.NewGuid()), default));

        ex.Code.Should().Be(InvitationRefusal.PayeeNotFound);
    }

    /// <summary>
    /// ★★ THE TARGET MUST BELONG TO THIS WORKSPACE. Without this check an administrator could name any
    /// user id in the system and hand a stranger from another tenant a window onto this tenant's money.
    /// </summary>
    [Fact]
    public async Task A_user_who_is_not_a_member_of_this_workspace_is_refused()
    {
        var h = Seed(nameof(A_user_who_is_not_a_member_of_this_workspace_is_refused));
        var payeeId = AddPayee(h, "EMP-3");

        var result = await h.Handler.Handle(new LinkUserToPayeeCommand(StrangerUser, payeeId), default);

        result.IsSuccess.Should().BeFalse();
        h.Db.Payees.Single(p => p.Id == payeeId).UserId.Should().BeNull();
    }

    [Fact]
    public async Task Passing_null_detaches_the_link()
    {
        var h = Seed(nameof(Passing_null_detaches_the_link));
        var payeeId = AddPayee(h, "EMP-4", MemberUser);

        var result = await h.Handler.Handle(new LinkUserToPayeeCommand(MemberUser, null), default);

        result.IsSuccess.Should().BeTrue();
        h.Db.Payees.Single(p => p.Id == payeeId).UserId.Should().BeNull();
    }

    /// <summary>
    /// ★★ RE-POINTING A LOGIN DETACHES THE OLD RECORD IN THE SAME WRITE. Attaching without detaching
    /// would leave two payees claiming the same user, and <c>Payee.UserId</c> has no unique index to
    /// catch it — the person would then be resolved by whichever row the query happened to return
    /// first, which is the quietest possible way to show somebody the wrong pay.
    /// </summary>
    [Fact]
    public async Task Moving_a_login_to_another_payee_leaves_only_one_link()
    {
        var h = Seed(nameof(Moving_a_login_to_another_payee_leaves_only_one_link));
        var oldPayeeId = AddPayee(h, "EMP-5", MemberUser);
        var newPayeeId = AddPayee(h, "EMP-6");

        var result = await h.Handler.Handle(new LinkUserToPayeeCommand(MemberUser, newPayeeId), default);

        result.IsSuccess.Should().BeTrue();
        h.Db.Payees.Single(p => p.Id == oldPayeeId).UserId.Should().BeNull();
        h.Db.Payees.Single(p => p.Id == newPayeeId).UserId.Should().Be(MemberUser);
        h.Db.Payees.Count(p => p.UserId == MemberUser).Should().Be(1);
    }

    /// <summary>
    /// ★ RE-SENDING THE SAME LINK IS A NO-OP, NOT A WRITE. A stale screen must not produce an audit
    /// entry saying somebody changed something they did not.
    /// </summary>
    [Fact]
    public async Task Re_linking_the_same_payee_changes_nothing()
    {
        var h = Seed(nameof(Re_linking_the_same_payee_changes_nothing));
        var payeeId = AddPayee(h, "EMP-7", MemberUser);
        var before = h.Db.Payees.Single(p => p.Id == payeeId).UpdatedAt;

        var result = await h.Handler.Handle(new LinkUserToPayeeCommand(MemberUser, payeeId), default);

        result.IsSuccess.Should().BeTrue();
        var after = h.Db.Payees.Single(p => p.Id == payeeId);
        after.UserId.Should().Be(MemberUser);
        after.UpdatedAt.Should().Be(before);
    }
}
