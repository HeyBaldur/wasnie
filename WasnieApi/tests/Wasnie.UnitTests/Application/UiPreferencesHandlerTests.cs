using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.UiPreferences;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Settings;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-78: UI choices ("saw the welcome", "snoozed 2FA") belong to the USER, on the server. What must hold: a write
/// is an upsert (one row per user and key), each user reads only their own, and the key/value stay bounded.
/// </summary>
public sealed class UiPreferencesHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public UiPreferencesHandlerTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _tenant.IsResolved.Returns(true);
        _user.UserId.Returns("user-a");
        _clock.UtcNowOffset.Returns(Now);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            _tenant,
            Substitute.For<MediatR.IPublisher>());
    }

    [Fact]
    public async Task Set_ThenGet_ReturnsTheValue()
    {
        (await Set("welcome-seen", "true")).IsSuccess.Should().BeTrue();

        var result = await Get();

        result.Value.Should().Contain("welcome-seen", "true");
    }

    [Fact]
    public async Task Set_Twice_UpdatesTheSameRow()
    {
        await Set("twofa-reminder", "{\"state\":\"snoozed\",\"until\":\"2026-09-17T10:00:00Z\"}");
        _clock.UtcNowOffset.Returns(Now.AddDays(1));
        await Set("twofa-reminder", "{\"state\":\"dismissed\"}");

        var rows = await _db.UserUiPreferences.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("{\"state\":\"dismissed\"}");
        rows[0].UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public async Task TwoUsersOfOneTenant_AreIndependent()
    {
        await Set("welcome-seen", "true");

        _user.UserId.Returns("user-b");
        (await Get()).Value.Should().BeEmpty("user B never closed the welcome");

        await Set("sandbox-intro-dismissed", "true");
        _user.UserId.Returns("user-a");
        (await Get()).Value.Should().ContainKey("welcome-seen").And.NotContainKey("sandbox-intro-dismissed");
    }

    [Fact]
    public async Task WithoutSignedInUser_Fails()
    {
        _user.UserId.Returns((string?)null);
        (await Get()).IsSuccess.Should().BeFalse();
        (await Set("welcome-seen", "true")).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("welcome-seen", true)]
    [InlineData("twofa-reminder", true)]
    [InlineData("tour.v2", true)]
    [InlineData("", false)]
    [InlineData("Welcome", false)]
    [InlineData("-leading-dash", false)]
    [InlineData("has space", false)]
    [InlineData("../etc", false)]
    public void KeyShape_IsValidated(string key, bool valid)
    {
        UserUiPreference.IsValidKey(key).Should().Be(valid);
        new SetUiPreferenceCommandValidator().Validate(new SetUiPreferenceCommand(key, "true")).IsValid.Should().Be(valid);
    }

    [Fact]
    public void KeyLongerThan64_IsRejected() =>
        UserUiPreference.IsValidKey(new string('a', 65)).Should().BeFalse();

    [Fact]
    public void ValueOverTheBound_IsRejected_ByValidatorAndDomain()
    {
        var tooLong = new string('x', UserUiPreference.MaxValueLength + 1);
        new SetUiPreferenceCommandValidator().Validate(new SetUiPreferenceCommand("welcome-seen", tooLong)).IsValid.Should().BeFalse();

        var act = () => UserUiPreference.Create(Guid.NewGuid(), TenantId, "u", "welcome-seen", tooLong, Now);
        act.Should().Throw<DomainException>();
    }

    private Task<Wasnie.Domain.Common.Results.Result<bool>> Set(string key, string value) =>
        new SetUiPreferenceHandler(_db, _tenant, _user, _clock).Handle(new SetUiPreferenceCommand(key, value), default);

    private Task<Wasnie.Domain.Common.Results.Result<IReadOnlyDictionary<string, string>>> Get() =>
        new GetUiPreferencesHandler(_db, _tenant, _user).Handle(new GetUiPreferencesQuery(), default);

    public void Dispose() => _db.Dispose();
}
