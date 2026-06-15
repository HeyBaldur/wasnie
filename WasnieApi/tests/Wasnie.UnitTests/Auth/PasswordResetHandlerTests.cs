using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.Handlers;
using Wasnie.Domain.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Auth;

public sealed class PasswordResetHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IIdentityService _identity;
    private readonly IEmailService _email;
    private readonly IAuditService _audit;
    private readonly ITokenService _tokenService;
    private readonly FakeClock _clock;
    private readonly IGuidGenerator _guid;
    private readonly IOptions<ResendOptions> _resendOptions;

    public PasswordResetHandlerTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(TenantId);
        tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        _identity = Substitute.For<IIdentityService>();
        _email = Substitute.For<IEmailService>();
        _audit = Substitute.For<IAuditService>();
        _tokenService = Substitute.For<ITokenService>();

        _clock = new FakeClock(Now.UtcDateTime);
        _guid = Substitute.For<IGuidGenerator>();
        _guid.NewGuid().Returns(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        _resendOptions = Options.Create(new ResendOptions
        {
            FrontendBaseUrl = "http://localhost:4200"
        });
    }

    public void Dispose() => _db.Dispose();

    private RequestPasswordResetCommandHandler BuildRequestHandler() =>
        new(_db, _identity, _email, _audit, _resendOptions, _clock, _guid,
            Substitute.For<ILogger<RequestPasswordResetCommandHandler>>());

    private ResetPasswordCommandHandler BuildResetHandler() =>
        new(_db, _identity, _tokenService, _audit, _clock);

    // ── RequestPasswordReset ─────────────────────────────────────────────────

    [Fact]
    public async Task RequestPasswordReset_UnknownEmail_ReturnsSuccessWithoutRevealingExistence()
    {
        _identity.FindUserIdByEmailAsync("nobody@example.com").Returns((string?)null);

        var result = await BuildRequestHandler().Handle(
            new RequestPasswordResetCommand("nobody@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _email.DidNotReceive().SendPasswordResetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RequestPasswordReset_UnconfirmedEmail_ReturnsSuccessWithoutSendingEmail()
    {
        const string userId = "user-1";
        _identity.FindUserIdByEmailAsync("unconfirmed@example.com").Returns(userId);
        _identity.IsEmailConfirmedAsync(userId).Returns(false);

        var result = await BuildRequestHandler().Handle(
            new RequestPasswordResetCommand("unconfirmed@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _email.DidNotReceive().SendPasswordResetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RequestPasswordReset_CooldownActive_SilentlyBlocksAndReturnsSuccess()
    {
        const string userId = "user-2";
        _identity.FindUserIdByEmailAsync("cool@example.com").Returns(userId);
        _identity.IsEmailConfirmedAsync(userId).Returns(true);
        _identity.GetClaimAsync(userId, Arg.Any<string>()).Returns((string?)null);

        // Seed a token created 30 seconds ago (within 2-minute cooldown).
        var recentToken = PasswordResetToken.Create(
            Guid.NewGuid(), userId,
            "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899",
            Now.AddMinutes(30),
            Now.AddSeconds(-30));
        _db.PasswordResetTokens.Add(recentToken);
        await _db.SaveChangesAsync();

        var result = await BuildRequestHandler().Handle(
            new RequestPasswordResetCommand("cool@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _email.DidNotReceive().SendPasswordResetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RequestPasswordReset_HourlyCapReached_SilentlyBlocksAndReturnsSuccess()
    {
        const string userId = "user-3";
        _identity.FindUserIdByEmailAsync("capped@example.com").Returns(userId);
        _identity.IsEmailConfirmedAsync(userId).Returns(true);

        // Seed 5 tokens created within last hour (cap = 5).
        for (int i = 0; i < 5; i++)
        {
            _db.PasswordResetTokens.Add(PasswordResetToken.Create(
                Guid.NewGuid(), userId,
                $"aabbccddeeff{i:D4}0011223344556677889900112233445566778899aabb{i:D4}",
                Now.AddMinutes(30),
                Now.AddMinutes(-10 - i)));
        }
        await _db.SaveChangesAsync();

        var result = await BuildRequestHandler().Handle(
            new RequestPasswordResetCommand("capped@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _email.DidNotReceive().SendPasswordResetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RequestPasswordReset_ValidRequest_SendsEmailAndReturnsSuccess()
    {
        const string userId = "user-4";
        const string email = "valid@example.com";
        _identity.FindUserIdByEmailAsync(email).Returns(userId);
        _identity.IsEmailConfirmedAsync(userId).Returns(true);
        _identity.GetClaimAsync(userId, "given_name").Returns("Alice");
        _identity.GetClaimAsync(userId, "locale").Returns("en");

        var result = await BuildRequestHandler().Handle(
            new RequestPasswordResetCommand(email), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _email.Received(1).SendPasswordResetAsync(
            email, "Alice", Arg.Any<string>(), "en");
        _db.PasswordResetTokens.Should().HaveCount(1);
    }

    // ── ResetPassword ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_ValidToken_ChangesPasswordAndRevokesTokens()
    {
        const string userId = "user-5";
        const string rawToken = "validrawtoken123456789012345678901234567890abc";

        // Pre-compute the hash the handler will look for.
        var tokenHash = ComputeHash(rawToken);
        var tokenRecord = PasswordResetToken.Create(
            Guid.NewGuid(), userId, tokenHash, Now.AddMinutes(30), Now.AddMinutes(-5));
        _db.PasswordResetTokens.Add(tokenRecord);
        await _db.SaveChangesAsync();

        _identity.ResetPasswordAsync(userId, Arg.Any<string>()).Returns(true);
        _identity.FindEmailByUserIdAsync(userId).Returns("user5@example.com");

        var result = await BuildResetHandler().Handle(
            new ResetPasswordCommand(userId, rawToken, "Str0ngPass!word"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tokenRecord.UsedAt.Should().NotBeNull();
        await _tokenService.Received(1).RevokeUserRefreshTokensAsync(userId);
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_ReturnsFailure()
    {
        const string userId = "user-6";
        const string rawToken = "expiredrawtoken1234567890123456789012345678901";

        var tokenHash = ComputeHash(rawToken);
        // Token expired 1 minute ago.
        var tokenRecord = PasswordResetToken.Create(
            Guid.NewGuid(), userId, tokenHash, Now.AddMinutes(-1), Now.AddMinutes(-35));
        _db.PasswordResetTokens.Add(tokenRecord);
        await _db.SaveChangesAsync();

        var result = await BuildResetHandler().Handle(
            new ResetPasswordCommand(userId, rawToken, "Str0ngPass!word"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("invalid or has expired");
        await _tokenService.DidNotReceive().RevokeUserRefreshTokensAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ResetPassword_UsedToken_ReturnsFailure()
    {
        const string userId = "user-7";
        const string rawToken = "usedrawtoken12345678901234567890123456789012ab";

        var tokenHash = ComputeHash(rawToken);
        var tokenRecord = PasswordResetToken.Create(
            Guid.NewGuid(), userId, tokenHash, Now.AddMinutes(30), Now.AddMinutes(-5));
        tokenRecord.MarkUsed(Now.AddMinutes(-3));
        _db.PasswordResetTokens.Add(tokenRecord);
        await _db.SaveChangesAsync();

        var result = await BuildResetHandler().Handle(
            new ResetPasswordCommand(userId, rawToken, "Str0ngPass!word"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("invalid or has expired");
    }

    [Fact]
    public async Task ResetPassword_TokenNotFound_ReturnsFailure()
    {
        var result = await BuildResetHandler().Handle(
            new ResetPasswordCommand("ghost-user", "nonexistenttoken123456", "Str0ngPass!word"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RequestPasswordReset_PreviousTokensInvalidated_WhenNewRequestSent()
    {
        const string userId = "user-8";
        const string email = "repeat@example.com";

        _identity.FindUserIdByEmailAsync(email).Returns(userId);
        _identity.IsEmailConfirmedAsync(userId).Returns(true);
        _identity.GetClaimAsync(userId, Arg.Any<string>()).Returns((string?)null);

        // Seed an old unused token (older than 2-minute cooldown so new request is allowed).
        var oldToken = PasswordResetToken.Create(
            Guid.NewGuid(), userId,
            "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899",
            Now.AddMinutes(20),
            Now.AddMinutes(-5));
        _db.PasswordResetTokens.Add(oldToken);
        await _db.SaveChangesAsync();

        await BuildRequestHandler().Handle(new RequestPasswordResetCommand(email), CancellationToken.None);

        // Old token should be marked as used (invalidated).
        oldToken.UsedAt.Should().NotBeNull();
    }

    // Helper to compute the same hash the handler uses.
    private static string ComputeHash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
