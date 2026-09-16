using System.Diagnostics;
using FluentAssertions;
using Wasnie.Application.Assistant.Common;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-74: the turn timer splits a turn into the provider's time and ours. What must hold: provider is the sum of the
/// model calls, ours is the rest, the first token is measured once, and a step that never ran is null — not zero.
/// </summary>
public sealed class AssistantTurnTimerTests
{
    private long _nowMs;

    private AssistantTurnTimer NewTimer() => new(() => _nowMs * Stopwatch.Frequency / 1000);

    [Fact]
    public void A_small_talk_turn_is_split_into_provider_and_ours()
    {
        var timer = NewTimer();

        _nowMs = 120; timer.RouterStarted();        // 120 ms of our own work before the first model call
        _nowMs = 2_120; timer.RouterEnded();        // router: 2 000
        _nowMs = 2_130; timer.DispatcherStarted();
        _nowMs = 5_130; timer.DispatcherEnded();    // dispatcher: 3 000
        _nowMs = 5_150; timer.AnswerStarted();
        _nowMs = 6_650; timer.AnswerFragment();     // first token after 1 500
        _nowMs = 6_900; timer.AnswerFragment();     // later fragments do not move it
        _nowMs = 7_150; timer.AnswerEnded();        // answer: 2 000
        _nowMs = 7_200;                             // persisting the answer

        var t = timer.Snapshot();

        t.TotalMs.Should().Be(7_200);
        t.RouterMs.Should().Be(2_000);
        t.DispatcherMs.Should().Be(3_000);
        t.AnswerMs.Should().Be(2_000);
        t.AnswerTimeToFirstTokenMs.Should().Be(1_500);
        // In series, the classifier span includes the 10 ms gap between the two calls.
        t.ClassifiersMs.Should().Be(5_010);
        t.ProviderMs.Should().Be(7_010);
        t.OursMs.Should().Be(190, "120 before the router + 20 before the answer + 50 after it");
        t.BeforeFirstModelCallMs.Should().Be(120);
        t.ToolMs.Should().BeNull("no tool ran — which is not the same as a tool that took no time");
    }

    [Fact]
    public void Parallel_classifiers_are_counted_once_not_summed()
    {
        var timer = NewTimer();

        _nowMs = 100; timer.RouterStarted(); timer.DispatcherStarted();
        _nowMs = 1_100; timer.RouterEnded();        // router: 1 000
        _nowMs = 1_900; timer.DispatcherEnded();    // dispatcher: 1 800, overlapping the router
        _nowMs = 1_910; timer.AnswerStarted();
        _nowMs = 2_410; timer.AnswerFragment();
        _nowMs = 2_910; timer.AnswerEnded();
        _nowMs = 2_950;

        var t = timer.Snapshot();

        t.RouterMs.Should().Be(1_000);
        t.DispatcherMs.Should().Be(1_800);
        t.ClassifiersMs.Should().Be(1_800, "the turn waited for the slower of the two, not for both added up");
        t.ProviderMs.Should().Be(2_800);
        t.OursMs.Should().Be(150).And.BePositive();
    }

    [Fact]
    public void A_step_that_failed_midway_is_measured_up_to_now()
    {
        var timer = NewTimer();

        _nowMs = 50; timer.RouterStarted();
        _nowMs = 60_050;                            // the provider timed out; RouterEnded never ran

        var t = timer.Snapshot();

        t.RouterMs.Should().Be(60_000);
        t.ProviderMs.Should().Be(60_000);
        t.DispatcherMs.Should().BeNull();
        t.AnswerTimeToFirstTokenMs.Should().BeNull();
    }

    [Fact]
    public void A_turn_refused_before_any_model_call_is_all_ours()
    {
        var timer = NewTimer();
        _nowMs = 35;

        var t = timer.Snapshot();

        t.ProviderMs.Should().Be(0);
        t.OursMs.Should().Be(35);
        t.BeforeFirstModelCallMs.Should().BeNull();
    }
}
