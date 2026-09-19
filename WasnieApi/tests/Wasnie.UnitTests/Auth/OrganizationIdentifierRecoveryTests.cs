using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.Handlers;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Identity;
using Wasnie.Domain.Entities;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Auth;

/// <summary>
/// KAN-93, Bug 4: recovering a forgotten Organization identifier.
///
/// ★★ THE ASSERTIONS ARE ABOUT WHAT WAS SENT, NOT WHAT WAS RETURNED, and that is the whole shape of
/// this feature. Every path returns the same success — an address that answered differently would let
/// anybody feed the endpoint emails and read back which companies use Incentra and who administers
/// them. So a test that checked the Result would pass no matter how badly the handler leaked; the only
/// observable that distinguishes the cases is the email, and that is what is checked here.
///
/// ★ THE MEMORY CACHE IS REAL, NOT A SUBSTITUTE. The cooldown is the second half of the rate limiting
/// (the route's limiter is per IP and cannot see one address hit from many), and a stubbed cache would
/// let the branch pass while the rule it implements went untested.
/// </summary>
public sealed class OrganizationIdentifierRecoveryTests : IDisposable
{
    private const string AdminEmail = "admin@acme.com";
    private const string RepEmail = "rep@acme.com";
    private const string AdminUser = "user-admin";
    private const string RepUser = "user-rep";

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly ApplicationDbContext _db;
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Guid _tenantId = Guid.NewGuid();

    public OrganizationIdentifierRecoveryTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(_tenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        _db.Tenants.Add(Tenant.Create("Acme Corp", "acme-corp", _tenantId, Now));
        _db.TenantUsers.Add(TenantUser.Create(
            Guid.NewGuid(), _tenantId, AdminUser, Roles.TenantAdmin, null, null, Now));
        _db.TenantUsers.Add(TenantUser.Create(
            Guid.NewGuid(), _tenantId, RepUser, Roles.Rep, null, null, Now));
        _db.SaveChanges();

        _identity.FindUserIdByEmailAsync(AdminEmail).Returns(AdminUser);
        _identity.FindUserIdByEmailAsync(RepEmail).Returns(RepUser);
        _identity.FindUserIdByEmailAsync(Arg.Is<string>(e => e != AdminEmail && e != RepEmail))
            .Returns((string?)null);
        _identity.IsEmailConfirmedAsync(Arg.Any<string>()).Returns(true);
        _identity.GetClaimAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    private RequestOrganizationIdentifierHandler Build()
    {
        var options = Substitute.For<IOptions<ResendOptions>>();
        options.Value.Returns(new ResendOptions { FrontendBaseUrl = "https://app.incentra.work" });

        return new RequestOrganizationIdentifierHandler(
            _db, _identity, _email, Substitute.For<IAuditService>(), _cache, options,
            Substitute.For<ILogger<RequestOrganizationIdentifierHandler>>());
    }

    private Task<Wasnie.Domain.Common.Results.Result<bool>> Ask(string email) =>
        Build().Handle(new RequestOrganizationIdentifierCommand(email), default);

    private Task NoEmailWasSent() =>
        _email.DidNotReceive().SendOrganizationIdentifierAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<OrganizationIdentifier>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task An_administrator_is_sent_the_identifier_of_the_workspace_they_administer()
    {
        var result = await Ask(AdminEmail);

        result.IsSuccess.Should().BeTrue();
        await _email.Received(1).SendOrganizationIdentifierAsync(
            AdminEmail,
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<OrganizationIdentifier>>(o =>
                o.Count == 1 && o[0].Slug == "acme-corp" && o[0].Name == "Acme Corp"),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ★★ THE ADMIN-ONLY RULE, AND IT IS THE ONE THE TICKET DECIDED. A rep gets no email — and the
    /// SAME success — so the screen's "ask your administrator" is the honest instruction rather than a
    /// consolation prize for a message that silently failed.
    /// </summary>
    [Fact]
    public async Task A_non_administrator_gets_no_email_and_the_same_answer()
    {
        var result = await Ask(RepEmail);

        result.IsSuccess.Should().BeTrue();
        await NoEmailWasSent();
    }

    [Fact]
    public async Task An_unknown_address_gets_no_email_and_the_same_answer()
    {
        var result = await Ask("nobody@example.com");

        result.IsSuccess.Should().BeTrue();
        await NoEmailWasSent();
    }

    /// <summary>
    /// ★ AN UNCONFIRMED MAILBOX HAS NOT BEEN PROVED TO BELONG TO ANYBODY, so it is not a channel a
    /// workspace's identifier may travel down. Same rule, same silence, as the password reset.
    /// </summary>
    [Fact]
    public async Task An_unconfirmed_address_gets_no_email()
    {
        _identity.IsEmailConfirmedAsync(AdminUser).Returns(false);

        var result = await Ask(AdminEmail);

        result.IsSuccess.Should().BeTrue();
        await NoEmailWasSent();
    }

    /// <summary>
    /// ★★ A DEACTIVATED ADMINISTRATOR IS NOT AN ADMINISTRATOR. Somebody whose access was closed must
    /// not be able to pull the workspace's identifier back out of the system — the role column alone
    /// would have let them, which is why the query filters on the active spec.
    /// </summary>
    [Fact]
    public async Task A_deactivated_administrator_gets_no_email()
    {
        var membership = _db.TenantUsers.Single(u => u.UserId == AdminUser);
        membership.Deactivate("someone", Now);
        await _db.SaveChangesAsync();

        var result = await Ask(AdminEmail);

        result.IsSuccess.Should().BeTrue();
        await NoEmailWasSent();
    }

    /// <summary>
    /// ★★ ADMINISTERING TWO WORKSPACES SENDS BOTH. The plural case is where getting it wrong hurts:
    /// sending the first of two looks like a working feature and leaves the reader locked out of the
    /// other company.
    /// </summary>
    [Fact]
    public async Task An_administrator_of_two_workspaces_is_sent_both()
    {
        var second = Guid.NewGuid();
        _db.Tenants.Add(Tenant.Create("Acme Polska", "acme-polska", second, Now));
        _db.TenantUsers.Add(TenantUser.Create(
            Guid.NewGuid(), second, AdminUser, Roles.TenantAdmin, null, null, Now));
        await _db.SaveChangesAsync();

        await Ask(AdminEmail);

        await _email.Received(1).SendOrganizationIdentifierAsync(
            AdminEmail,
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<OrganizationIdentifier>>(o =>
                o.Count == 2 && o.Any(x => x.Slug == "acme-corp") && o.Any(x => x.Slug == "acme-polska")),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ★★ THE PER-ADDRESS COOLDOWN, which the route's IP limiter cannot provide. Without it one
    /// address can be mailed repeatedly from rotating IPs — spam delivered by our own domain.
    /// </summary>
    [Fact]
    public async Task A_second_request_within_the_cooldown_sends_nothing()
    {
        await Ask(AdminEmail);
        var result = await Ask(AdminEmail);

        result.IsSuccess.Should().BeTrue();
        await _email.Received(1).SendOrganizationIdentifierAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<OrganizationIdentifier>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ★ THE COOLDOWN IS KEYED ON THE ADDRESS, CASE-INSENSITIVELY. "Admin@acme.com" is the same
    /// mailbox, and a key that did not fold case would be bypassed by the shift key.
    /// </summary>
    [Fact]
    public async Task The_cooldown_is_not_bypassed_by_changing_the_case()
    {
        await Ask(AdminEmail);
        await Ask(AdminEmail.ToUpperInvariant());

        await _email.Received(1).SendOrganizationIdentifierAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<OrganizationIdentifier>>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
