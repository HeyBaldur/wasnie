using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Auth.Commands;
using Wasnie.Application.Features.Auth.Handlers;
using Wasnie.Application.Features.Auth.Queries;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Auth;

public sealed class TwoFactorHandlerTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly IIdentityService _identity;
    private readonly ITokenService _tokenService;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantContext _tenantContext;

    public TwoFactorHandlerTests()
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
        _tokenService = Substitute.For<ITokenService>();
        _audit = Substitute.For<IAuditService>();
        _tenantContext = tenantCtx;

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns("user-1");
        _currentUser.Email.Returns("alice@example.com");
        _currentUser.IsAuthenticated.Returns(true);
    }

    public void Dispose() => _db.Dispose();

    // ── GetTwoFactorStatusHandler ────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_WhenEnabled_ReturnsTrueWithCount()
    {
        _identity.IsTwoFactorEnabledAsync("user-1").Returns(true);
        _identity.CountRecoveryCodesAsync("user-1").Returns(7);

        var handler = new GetTwoFactorStatusHandler(_currentUser, _identity);
        var result = await handler.Handle(new GetTwoFactorStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsEnabled.Should().BeTrue();
        result.Value!.RecoveryCodeCount.Should().Be(7);
    }

    [Fact]
    public async Task GetStatus_WhenDisabled_ReturnsFalseWithZeroCount()
    {
        _identity.IsTwoFactorEnabledAsync("user-1").Returns(false);

        var handler = new GetTwoFactorStatusHandler(_currentUser, _identity);
        var result = await handler.Handle(new GetTwoFactorStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsEnabled.Should().BeFalse();
        result.Value!.RecoveryCodeCount.Should().Be(0);
        await _identity.DidNotReceive().CountRecoveryCodesAsync(Arg.Any<string>());
    }

    // ── GetTwoFactorSetupHandler ─────────────────────────────────────────────

    [Fact]
    public async Task GetSetup_ReturnsSecretAndOtpauthUri()
    {
        _identity.GetOrCreateTotpSecretAsync("user-1").Returns("ABCD EFGH IJKL MNOP");

        var handler = new GetTwoFactorSetupHandler(_currentUser, _identity);
        var result = await handler.Handle(new GetTwoFactorSetupQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Secret.Should().Be("ABCD EFGH IJKL MNOP");
        result.Value!.OtpauthUri.Should().StartWith("otpauth://totp/");
        result.Value!.OtpauthUri.Should().Contain("Wasnie");
        result.Value!.OtpauthUri.Should().Contain("ABCD EFGH IJKL MNOP");
    }

    [Fact]
    public async Task GetSetup_WhenUserNotFound_ReturnsFailure()
    {
        _currentUser.UserId.Returns((string?)null);

        var handler = new GetTwoFactorSetupHandler(_currentUser, _identity);
        var result = await handler.Handle(new GetTwoFactorSetupQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // ── EnableTwoFactorCommandHandler ────────────────────────────────────────

    [Fact]
    public async Task Enable_ValidCode_ReturnsRecoveryCodes()
    {
        var codes = new[] { "code1", "code2", "code3" };
        _identity.EnableTwoFactorAsync("user-1", "123456")
            .Returns((true, (IEnumerable<string>)codes));

        var handler = new EnableTwoFactorCommandHandler(_currentUser, _tenantContext, _identity, _audit);
        var result = await handler.Handle(new EnableTwoFactorCommand("123456"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecoveryCodes.Should().BeEquivalentTo(codes);
        await _audit.Received(1).LogAsync(
            Arg.Is<Wasnie.Application.Common.DTOs.AuditEntry>(e => e.Action == "TWO_FACTOR_ENABLED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enable_InvalidCode_ReturnsFailure()
    {
        _identity.EnableTwoFactorAsync("user-1", "000000")
            .Returns((false, Enumerable.Empty<string>()));

        var handler = new EnableTwoFactorCommandHandler(_currentUser, _tenantContext, _identity, _audit);
        var result = await handler.Handle(new EnableTwoFactorCommand("000000"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid verification code");
    }

    // ── DisableTwoFactorCommandHandler ───────────────────────────────────────

    [Fact]
    public async Task Disable_CorrectPasswordAndCode_Succeeds()
    {
        _identity.VerifyPasswordAsync("user-1", "Pass!word1").Returns(true);
        _identity.VerifyTotpCodeAsync("user-1", "123456").Returns(true);
        _identity.DisableTwoFactorAsync("user-1").Returns(true);

        var handler = new DisableTwoFactorCommandHandler(_currentUser, _tenantContext, _identity, _audit);
        var result = await handler.Handle(new DisableTwoFactorCommand("Pass!word1", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _audit.Received(1).LogAsync(
            Arg.Is<Wasnie.Application.Common.DTOs.AuditEntry>(e => e.Action == "TWO_FACTOR_DISABLED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disable_WrongPassword_ReturnsFailure()
    {
        _identity.VerifyPasswordAsync("user-1", "wrong").Returns(false);

        var handler = new DisableTwoFactorCommandHandler(_currentUser, _tenantContext, _identity, _audit);
        var result = await handler.Handle(new DisableTwoFactorCommand("wrong", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("password");
        await _identity.DidNotReceive().VerifyTotpCodeAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Disable_WrongCode_ReturnsFailure()
    {
        _identity.VerifyPasswordAsync("user-1", "Pass!word1").Returns(true);
        _identity.VerifyTotpCodeAsync("user-1", "000000").Returns(false);

        var handler = new DisableTwoFactorCommandHandler(_currentUser, _tenantContext, _identity, _audit);
        var result = await handler.Handle(new DisableTwoFactorCommand("Pass!word1", "000000"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid verification code");
        await _identity.DidNotReceive().DisableTwoFactorAsync(Arg.Any<string>());
    }

    // ── RegenerateRecoveryCodesCommandHandler ────────────────────────────────

    [Fact]
    public async Task RegenerateCodes_WhenEnabled_ReturnsCodes()
    {
        var newCodes = new[] { "new1", "new2", "new3" };
        _identity.IsTwoFactorEnabledAsync("user-1").Returns(true);
        _identity.VerifyPasswordAsync("user-1", "Pass!word1").Returns(true);
        _identity.VerifyTotpCodeAsync("user-1", "123456").Returns(true);
        _identity.GenerateRecoveryCodesAsync("user-1", 10).Returns((IEnumerable<string>?)newCodes);

        var handler = new RegenerateRecoveryCodesCommandHandler(_currentUser, _tenantContext, _identity, _audit);
        var result = await handler.Handle(new RegenerateRecoveryCodesCommand("Pass!word1", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(newCodes);
        await _audit.Received(1).LogAsync(
            Arg.Is<Wasnie.Application.Common.DTOs.AuditEntry>(e => e.Action == "RECOVERY_CODES_REGENERATED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegenerateCodes_WhenNotEnabled_ReturnsFailure()
    {
        _identity.IsTwoFactorEnabledAsync("user-1").Returns(false);

        var handler = new RegenerateRecoveryCodesCommandHandler(_currentUser, _tenantContext, _identity, _audit);
        var result = await handler.Handle(new RegenerateRecoveryCodesCommand("Pass!word1", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not enabled");
    }

    // ── VerifyTwoFactorLoginCommandHandler ───────────────────────────────────

    [Fact]
    public async Task VerifyLogin_InvalidChallengeToken_ReturnsFailure()
    {
        _tokenService.ValidateTwoFactorChallengeToken("bad-token").Returns((string?)null);

        var handler = new VerifyTwoFactorLoginCommandHandler(_tokenService, _identity, _db, _audit);
        var result = await handler.Handle(
            new VerifyTwoFactorLoginCommand("bad-token", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task VerifyLogin_ValidCodeAfterValidToken_ReturnsFullSession()
    {
        const string userId = "user-42";
        const string email = "bob@example.com";
        var tenantId = Guid.NewGuid();

        _tokenService.ValidateTwoFactorChallengeToken("valid-challenge").Returns(userId);
        _identity.FindEmailByUserIdAsync(userId).Returns(email);
        _identity.GetTenantIdClaimAsync(userId).Returns(tenantId.ToString());
        _identity.VerifyTotpCodeAsync(userId, "123456").Returns(true);
        _identity.GetUserRolesAsync(userId).Returns(new List<string> { "Admin" });

        _tokenService.GenerateTokenPairAsync(
            userId, email, tenantId, Arg.Any<IList<string>>())
            .Returns(new Wasnie.Application.Features.Auth.DTOs.TokenPairDto(
                "access", "refresh",
                DateTimeOffset.UtcNow.AddMinutes(15),
                DateTimeOffset.UtcNow.AddDays(7)));

        var tenant = Wasnie.Domain.Entities.Tenant.Create("Acme", "acme", tenantId, DateTimeOffset.UtcNow);
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var handler = new VerifyTwoFactorLoginCommandHandler(_tokenService, _identity, _db, _audit);
        var result = await handler.Handle(
            new VerifyTwoFactorLoginCommand("valid-challenge", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Tokens.Should().NotBeNull();
        result.Value!.RequiresTwoFactor.Should().BeFalse();
        await _audit.Received(1).LogAsync(
            Arg.Is<Wasnie.Application.Common.DTOs.AuditEntry>(e => e.Action == "TWO_FACTOR_LOGIN_SUCCESS"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyLogin_RecoveryCode_LogsRecoveryCodeUsed()
    {
        const string userId = "user-43";
        const string email = "carol@example.com";
        var tenantId = Guid.NewGuid();

        _tokenService.ValidateTwoFactorChallengeToken("valid-challenge").Returns(userId);
        _identity.FindEmailByUserIdAsync(userId).Returns(email);
        _identity.GetTenantIdClaimAsync(userId).Returns(tenantId.ToString());
        _identity.RedeemRecoveryCodeAsync(userId, "ABCD1234").Returns(true);
        _identity.GetUserRolesAsync(userId).Returns(new List<string>());

        _tokenService.GenerateTokenPairAsync(
            userId, email, tenantId, Arg.Any<IList<string>>())
            .Returns(new Wasnie.Application.Features.Auth.DTOs.TokenPairDto(
                "access", "refresh",
                DateTimeOffset.UtcNow.AddMinutes(15),
                DateTimeOffset.UtcNow.AddDays(7)));

        var tenant = Wasnie.Domain.Entities.Tenant.Create("Acme2", "acme2", tenantId, DateTimeOffset.UtcNow);
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var handler = new VerifyTwoFactorLoginCommandHandler(_tokenService, _identity, _db, _audit);
        var result = await handler.Handle(
            new VerifyTwoFactorLoginCommand("valid-challenge", "ABCD1234", IsRecoveryCode: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _audit.Received(1).LogAsync(
            Arg.Is<Wasnie.Application.Common.DTOs.AuditEntry>(e => e.Action == "RECOVERY_CODE_USED"),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(
            Arg.Is<Wasnie.Application.Common.DTOs.AuditEntry>(e => e.Action == "TWO_FACTOR_LOGIN_SUCCESS"),
            Arg.Any<CancellationToken>());
    }
}
