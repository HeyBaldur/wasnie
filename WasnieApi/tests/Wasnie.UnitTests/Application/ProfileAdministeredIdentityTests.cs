using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Profile.Commands;
using Wasnie.Application.Features.Profile.Handlers;
using Wasnie.Application.Features.Profile.Queries;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// A login an administrator attached to a payee does not rename itself.
///
/// ★★ WHY THE SERVER AND NOT ONLY THE SCREEN. The profile endpoints carry NO permission — they are
/// self-service by design, `[Authorize]` and nothing more. So hiding the two cards on /profile without
/// these guards would be a screen saying no in front of an endpoint saying yes, which is the same
/// mistake as hiding a menu entry and leaving the URL reachable. These tests exist because that is the
/// half nobody notices is missing: the product LOOKS correct either way.
///
/// ★★ AND THE DISCRIMINATOR IS THE PAYEE LINK, NOT THE ROLE. A Manager who is also paid has exactly
/// the same problem, and an administrator with no payee record has none — so the tests below are
/// written about the LINK, and there is not a role name anywhere in this file. If somebody later
/// "simplifies" this to a Rep check, the unlinked cases here are what fails.
/// </summary>
public sealed class ProfileAdministeredIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private const string MyUser = "user-mine";

    private sealed record Harness(ApplicationDbContext Db, Guid TenantId);

    private static Harness Seed(string dbName, bool linked)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx, Substitute.For<IPublisher>());

        var payee = Payee.Create(tenantId, "Ana Garcia", "EMP-MINE", "ana@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);

        // ★ The ONE difference between every pair of cases below.
        if (linked) payee.LinkToUser(MyUser, "test", Now);

        db.Payees.Add(payee);
        db.SaveChanges();

        return new Harness(db, tenantId);
    }

    private static ICurrentUserService CurrentUser()
    {
        var u = Substitute.For<ICurrentUserService>();
        u.UserId.Returns(MyUser);
        u.Email.Returns("ana@acme.com");
        return u;
    }

    private static ITenantContext TenantOf(Harness h)
    {
        var t = Substitute.For<ITenantContext>();
        t.TenantId.Returns(h.TenantId);
        return t;
    }

    // ══ Renaming ═════════════════════════════════════════════════════════

    private static (UpdateProfileNameHandler Handler, IIdentityService Identity) NameHandler(Harness h)
    {
        var identity = Substitute.For<IIdentityService>();
        identity.GetClaimAsync(Arg.Any<string>(), Arg.Any<string>()).Returns("Ana");
        identity.UpdateClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        return (new UpdateProfileNameHandler(
            CurrentUser(), identity, h.Db, Substitute.For<IAuditService>(), TenantOf(h)), identity);
    }

    /// <summary>
    /// ★★ THE CASE THE GUARD EXISTS FOR. Their name is the name on a payslip; a second version of it,
    /// maintained by them, would leave nothing to say which one is right.
    /// </summary>
    [Fact]
    public async Task An_administered_account_cannot_rename_itself()
    {
        var h = Seed(nameof(An_administered_account_cannot_rename_itself), linked: true);
        var (handler, identity) = NameHandler(h);

        var result = await handler.Handle(new UpdateProfileNameCommand("Anita", "G"), default);

        result.IsSuccess.Should().BeFalse();

        // ★ AND NOTHING WAS WRITTEN. A refusal that still updated one of the two claims would leave
        // half a rename behind — worse than either answer.
        await identity.DidNotReceive().UpdateClaimAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// ★★ AND THE OTHER HALF, WHICH IS THE ONE THAT WOULD BREAK QUIETLY. Nobody maintains a name for an
    /// unlinked account, so refusing it would leave that person unable to fill in their own — including
    /// every administrator who is not also a payee.
    /// </summary>
    [Fact]
    public async Task An_unlinked_account_renames_itself_normally()
    {
        var h = Seed(nameof(An_unlinked_account_renames_itself_normally), linked: false);
        var (handler, identity) = NameHandler(h);

        var result = await handler.Handle(new UpdateProfileNameCommand("Anita", "G"), default);

        result.IsSuccess.Should().BeTrue();
        await identity.Received().UpdateClaimAsync(MyUser, "given_name", "Anita");
    }

    // ══ Changing the sign-in address ═════════════════════════════════════

    private static (RequestEmailChangeHandler Handler, IEmailService Email) EmailHandler(Harness h)
    {
        var identity = Substitute.For<IIdentityService>();
        identity.FindUserIdByEmailAsync(Arg.Any<string>()).Returns((string?)null);
        identity.GetClaimAsync(Arg.Any<string>(), Arg.Any<string>()).Returns("Ana");

        var email = Substitute.For<IEmailService>();

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        var guid = Substitute.For<IGuidGenerator>();
        guid.NewGuid().Returns(_ => Guid.NewGuid());

        var options = Options.Create(new ResendOptions { FrontendBaseUrl = "https://app.test" });

        return (new RequestEmailChangeHandler(
            CurrentUser(), identity, h.Db, email, Substitute.For<IAuditService>(),
            options, TenantOf(h), clock, guid,
            NullLogger<RequestEmailChangeHandler>.Instance), email);
    }

    /// <summary>
    /// ★★ THE ADDRESS IS ALSO THE LOGIN, so moving it on an administered account is the administrator's
    /// call. Asserted on the MAIL and the TOKEN, not on the Result alone: a refusal that had already
    /// sent the confirmation would hand the person a working link to a change the product then denies.
    /// </summary>
    [Fact]
    public async Task An_administered_account_cannot_move_its_sign_in_address()
    {
        var h = Seed(nameof(An_administered_account_cannot_move_its_sign_in_address), linked: true);
        var (handler, email) = EmailHandler(h);

        var result = await handler.Handle(new RequestEmailChangeCommand("new@acme.com"), default);

        result.IsSuccess.Should().BeFalse();
        h.Db.EmailChangeTokens.Should().BeEmpty();
        await email.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>★ An unlinked account still moves its own address, confirmation mail and all.</summary>
    [Fact]
    public async Task An_unlinked_account_can_still_move_its_sign_in_address()
    {
        var h = Seed(nameof(An_unlinked_account_can_still_move_its_sign_in_address), linked: false);
        var (handler, email) = EmailHandler(h);

        var result = await handler.Handle(new RequestEmailChangeCommand("new@acme.com"), default);

        result.IsSuccess.Should().BeTrue();
        await email.Received(1).SendEmailChangeConfirmationAsync(
            "new@acme.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ══ What the screen is told ══════════════════════════════════════════

    private static GetProfileHandler ProfileHandler(Harness h)
    {
        var identity = Substitute.For<IIdentityService>();
        identity.GetClaimAsync(Arg.Any<string>(), Arg.Any<string>()).Returns("Ana");

        var clock = Substitute.For<IClock>();
        clock.UtcNowOffset.Returns(Now);

        return new GetProfileHandler(CurrentUser(), identity, h.Db, TenantOf(h), clock);
    }

    /// <summary>
    /// ★★ THE FLAG IS WHAT HIDES THE CARDS, so it has to be the same answer the two guards give. If it
    /// ever disagreed, the screen would offer an edit the server refuses — the exact shape of the
    /// simulator defect earlier in this branch, where the permission moved and nothing told the UI.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_profile_reports_whether_its_identity_is_administered(bool linked)
    {
        var h = Seed($"{nameof(The_profile_reports_whether_its_identity_is_administered)}-{linked}", linked);

        var dto = await ProfileHandler(h).Handle(new GetProfileQuery(), default);

        dto.IdentityManagedByAdministrator.Should().Be(linked);
    }

    /// <summary>
    /// ★ SOMEBODY ELSE'S PAYEE IS NOT MY LINK. The check is anchored on this user id; a workspace full
    /// of linked colleagues must not make an unlinked administrator's own profile read-only.
    /// </summary>
    [Fact]
    public async Task A_colleagues_payee_link_does_not_administer_my_identity()
    {
        var h = Seed(nameof(A_colleagues_payee_link_does_not_administer_my_identity), linked: false);

        var other = Payee.Create(h.TenantId, "Bruno Silva", "EMP-OTHER", "bruno@acme.com",
            new DateOnly(2020, 1, 1), "test", Guid.NewGuid(), Now);
        other.LinkToUser("somebody-else", "test", Now);
        h.Db.Payees.Add(other);
        h.Db.SaveChanges();

        var dto = await ProfileHandler(h).Handle(new GetProfileQuery(), default);

        dto.IdentityManagedByAdministrator.Should().BeFalse();
    }
}
