using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Infrastructure.Services;

namespace Wasnie.UnitTests.Auth;

/// <summary>
/// Tests the real TokenService JWT implementation for the 2FA challenge token.
/// Handler tests mock ITokenService and cannot catch JWT parsing bugs such as
/// the InboundClaimTypeMap "sub" remapping issue.
/// </summary>
public sealed class TokenServiceChallengeTokenTests
{
    private readonly TokenService _sut;
    private readonly IClock _clock;

    public TokenServiceChallengeTokenTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test-secret-key-that-is-long-enough-for-hmac-sha256",
                ["JwtSettings:Issuer"] = "WasnieApi",
                ["JwtSettings:Audience"] = "WasnieUi",
                ["JwtSettings:ExpiryMinutes"] = "15",
            })
            .Build();

        _clock = Substitute.For<IClock>();
        _clock.UtcNowOffset.Returns(DateTimeOffset.UtcNow);

        var guid = Substitute.For<IGuidGenerator>();
        guid.NewGuid().Returns(Guid.NewGuid());

        // IApplicationDbContext not needed for challenge token methods
        var db = Substitute.For<IApplicationDbContext>();

        _sut = new TokenService(config, db, _clock, guid);
    }

    [Fact]
    public void ValidateChallengeToken_WithFreshToken_ReturnsCorrectUserId()
    {
        // Regression guard: JwtSecurityTokenHandler remaps "sub" → ClaimTypes.NameIdentifier
        // by default; InboundClaimTypeMap.Clear() must be called before ValidateToken.
        var userId = "user-123";

        var token = _sut.GenerateTwoFactorChallengeToken(userId);
        var result = _sut.ValidateTwoFactorChallengeToken(token);

        result.Should().Be(userId);
    }

    [Fact]
    public void ValidateChallengeToken_WithExpiredToken_ReturnsNull()
    {
        var userId = "user-123";

        // Issue token at t=-6min, so it expires at t=-1min
        _clock.UtcNowOffset.Returns(DateTimeOffset.UtcNow.AddMinutes(-6));
        var token = _sut.GenerateTwoFactorChallengeToken(userId);

        // Reset clock to now so the validator checks against "now"
        _clock.UtcNowOffset.Returns(DateTimeOffset.UtcNow);
        var result = _sut.ValidateTwoFactorChallengeToken(token);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateChallengeToken_WithTamperedToken_ReturnsNull()
    {
        var userId = "user-123";
        var token = _sut.GenerateTwoFactorChallengeToken(userId);

        // Tamper with the signature segment
        var parts = token.Split('.');
        parts[2] = parts[2][..^4] + "XXXX";
        var tamperedToken = string.Join('.', parts);

        var result = _sut.ValidateTwoFactorChallengeToken(tamperedToken);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateChallengeToken_WithEmptyString_ReturnsNull()
    {
        var result = _sut.ValidateTwoFactorChallengeToken(string.Empty);
        result.Should().BeNull();
    }
}
