using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.Handlers;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Auth;

/// <summary>
/// Covers the acceptance criteria added to KAN-21: the user must learn that their access is
/// temporarily blocked, without that message telling an attacker which addresses are registered.
///
/// These tests drive the real <see cref="LoginAttemptTracker"/>, not a substitute. A stubbed
/// tracker would let the handler's branches pass while the counting rule that actually produces
/// the indistinguishability went untested.
/// </summary>
public sealed class LoginLockoutNoticeTests : IDisposable
{
    private const string KnownEmail = "known@example.com";
    private const string UnknownEmail = "nobody@example.com";
    private const string UserId = "user-1";
    private const string Password = "wrong-password";

    private static readonly DateTime Now = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _db;
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeClock _clock = new(Now);

    public LoginLockoutNoticeTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.NewGuid());
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        // Every credential check fails — that is the situation under test.
        _identity.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(((bool, string?, string?))(false, null, null));

        _identity.FindUserIdByEmailAsync(KnownEmail).Returns(UserId);
        _identity.FindUserIdByEmailAsync(UnknownEmail).Returns((string?)null);
        _identity.GetClaimAsync(UserId, "given_name").Returns("Ada");
        _identity.GetClaimAsync(UserId, "locale").Returns("es");
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private LoginCommandHandler BuildHandler() =>
        new(_identity, _tokens, _db, _audit,
            new LoginAttemptTracker(_cache, _clock),
            _email,
            Options.Create(new ResendOptions { FrontendBaseUrl = "http://localhost:4200" }),
            Substitute.For<ILogger<LoginCommandHandler>>());

    private async Task<string?> AttemptAsync(LoginCommandHandler handler, string email, int times)
    {
        string? message = null;
        for (var i = 0; i < times; i++)
        {
            var result = await handler.Handle(new LoginCommand(email, Password), CancellationToken.None);
            result.IsSuccess.Should().BeFalse();
            message = result.Error;
        }

        return message;
    }

    [Fact]
    public async Task BelowTheThreshold_TheMessageStaysGeneric()
    {
        var message = await AttemptAsync(BuildHandler(), KnownEmail, 4);

        message.Should().Be("Invalid credentials.");
    }

    [Fact]
    public async Task AtTheThreshold_TheUserIsToldTheAccountIsBlockedAndForHowLong()
    {
        var message = await AttemptAsync(BuildHandler(), KnownEmail, 5);

        message.Should().Be("ACCOUNT_LOCKED:15");
    }

    [Fact]
    public async Task AnUnknownAddress_ProducesAByteForByteIdenticalResponse()
    {
        // The AC that governs everything else here: the message must not answer
        // "is this address registered?".
        var known = await AttemptAsync(BuildHandler(), KnownEmail, 5);
        var unknown = await AttemptAsync(BuildHandler(), UnknownEmail, 5);

        unknown.Should().Be(known);
        unknown.Should().Be("ACCOUNT_LOCKED:15");
    }

    [Fact]
    public async Task TheLockoutWarningIsSentOnce_ToTheAccountHolder()
    {
        await AttemptAsync(BuildHandler(), KnownEmail, 5);

        await _email.Received(1).SendAccountLockedAsync(
            KnownEmail, "Ada", "http://localhost:4200/auth/forgot-password", 15, "es",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatedAttemptsInsideOneWindow_DoNotSendASecondWarning()
    {
        // Otherwise the feature becomes a mail cannon aimed at the victim.
        var handler = BuildHandler();

        await AttemptAsync(handler, KnownEmail, 30);

        await _email.Received(1).SendAccountLockedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewWindowAfterTheOldOneExpired_WarnsAgain()
    {
        var handler = BuildHandler();
        await AttemptAsync(handler, KnownEmail, 5);

        _clock.Advance(TimeSpan.FromMinutes(16));
        await AttemptAsync(handler, KnownEmail, 5);

        await _email.Received(2).SendAccountLockedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownAddress_NeverTriggersAnEmail()
    {
        await AttemptAsync(BuildHandler(), UnknownEmail, 10);

        await _email.DidNotReceive().SendAccountLockedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedEmailDelivery_DoesNotChangeWhatTheCallerSees()
    {
        // A login that failed differently when the mail server was down would be its own oracle.
        _email.SendAccountLockedAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Resend is down"));

        var message = await AttemptAsync(BuildHandler(), KnownEmail, 5);

        message.Should().Be("ACCOUNT_LOCKED:15");
    }

    [Fact]
    public async Task TheCountdownShrinksAsTheWindowRunsDown()
    {
        var handler = BuildHandler();
        await AttemptAsync(handler, KnownEmail, 5);

        _clock.Advance(TimeSpan.FromMinutes(10));
        var message = await AttemptAsync(handler, KnownEmail, 1);

        message.Should().Be("ACCOUNT_LOCKED:5");
    }

    [Fact]
    public async Task TheDefaultLocaleIsUsedWhenTheUserHasNone()
    {
        _identity.GetClaimAsync(UserId, "locale").Returns((string?)null);
        _identity.GetClaimAsync(UserId, "given_name").Returns((string?)null);

        await AttemptAsync(BuildHandler(), KnownEmail, 5);

        await _email.Received(1).SendAccountLockedAsync(
            KnownEmail, "known", Arg.Any<string>(), 15, "en", Arg.Any<CancellationToken>());
    }
}
