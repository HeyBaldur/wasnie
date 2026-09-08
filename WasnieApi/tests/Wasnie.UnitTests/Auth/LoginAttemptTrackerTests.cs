using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Infrastructure.Identity;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Auth;

public sealed class LoginAttemptTrackerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeClock _clock = new(Now);

    public void Dispose() => _cache.Dispose();

    private ILoginAttemptTracker Build() => new LoginAttemptTracker(_cache, _clock);

    private static LoginAttemptState RecordN(ILoginAttemptTracker t, string email, int n)
    {
        var state = default(LoginAttemptState);
        for (var i = 0; i < n; i++) state = t.RecordFailure(email);
        return state;
    }

    [Fact]
    public void FourFailures_DoNotLock()
    {
        var state = RecordN(Build(), "a@example.com", 4);

        state.IsLocked.Should().BeFalse();
        state.JustLocked.Should().BeFalse();
        state.RetryAfterMinutes.Should().Be(0);
    }

    [Fact]
    public void FifthFailure_LocksAndReportsTheTransition()
    {
        var state = RecordN(Build(), "a@example.com", 5);

        state.IsLocked.Should().BeTrue();
        state.JustLocked.Should().BeTrue();
        state.RetryAfterMinutes.Should().Be(15);
    }

    [Fact]
    public void FurtherFailuresInsideTheWindow_StayLockedButNeverRepeatTheTransition()
    {
        // This is what holds the warning email to one per lockout. If JustLocked came back true
        // a second time, a sustained attack would mail the victim on every attempt.
        var tracker = Build();
        RecordN(tracker, "a@example.com", 5);

        for (var i = 0; i < 20; i++)
        {
            var state = tracker.RecordFailure("a@example.com");
            state.IsLocked.Should().BeTrue();
            state.JustLocked.Should().BeFalse();
        }
    }

    [Fact]
    public void AttemptsInsideTheWindow_DoNotPushTheDeadlineBack()
    {
        var tracker = Build();
        RecordN(tracker, "a@example.com", 5);

        _clock.Advance(TimeSpan.FromMinutes(10));
        var state = tracker.RecordFailure("a@example.com");

        // 5 minutes left of the original window, not 15 restarted.
        state.RetryAfterMinutes.Should().Be(5);
    }

    [Fact]
    public void AfterTheWindowExpires_TheCountStartsOver()
    {
        var tracker = Build();
        RecordN(tracker, "a@example.com", 5);

        _clock.Advance(TimeSpan.FromMinutes(16));

        var first = tracker.RecordFailure("a@example.com");
        first.IsLocked.Should().BeFalse("the previous window closed and this is attempt 1 of a new one");

        var fifth = RecordN(tracker, "a@example.com", 4);
        fifth.IsLocked.Should().BeTrue();
        fifth.JustLocked.Should().BeTrue("a new window is a new lockout, and warrants a new warning");
    }

    [Fact]
    public void SuccessfulSignIn_ClearsTheCounter()
    {
        var tracker = Build();
        RecordN(tracker, "a@example.com", 4);

        tracker.Reset("a@example.com");

        RecordN(tracker, "a@example.com", 4).IsLocked.Should().BeFalse();
    }

    [Fact]
    public void CountersAreIndependentPerAddress()
    {
        var tracker = Build();
        RecordN(tracker, "a@example.com", 4);

        tracker.RecordFailure("b@example.com").IsLocked.Should().BeFalse();
    }

    [Fact]
    public void CaseAndSurroundingSpaceDoNotSplitTheCounter()
    {
        // Otherwise five attempts become fifteen, and the lockout notice never fires.
        var tracker = Build();
        tracker.RecordFailure("a@example.com");
        tracker.RecordFailure("A@Example.com");
        tracker.RecordFailure("  a@example.com  ");
        tracker.RecordFailure("a@EXAMPLE.COM");

        tracker.RecordFailure("a@example.com").IsLocked.Should().BeTrue();
    }

    [Fact]
    public void AnAddressThatBelongsToNobody_IsCountedTheSameWay()
    {
        // The tracker never asks whether the account exists. That is the whole point: it is what
        // makes the login response identical for a registered and an unregistered address.
        var state = RecordN(Build(), "nobody-at-all@example.com", 5);

        state.IsLocked.Should().BeTrue();
        state.RetryAfterMinutes.Should().Be(15);
    }
}
