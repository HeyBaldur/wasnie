using FluentAssertions;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Subscription;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// KAN-77 — the one rule that decides who is in. Every reader (paywall, metered-feature gate, HubSpot sync,
/// assistant allowance, account endpoint) goes through it, so these cases are the contract of all of them.
/// </summary>
public sealed class AccountAccessPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Trialing)]
    public void A_live_stripe_subscription_is_Active_with_or_without_a_trial(SubscriptionStatus status)
    {
        AccountAccessPolicy.Resolve(null, status, hasStripeSubscription: true, Now).State.Should().Be(AccountAccessState.Active);
        AccountAccessPolicy.Resolve(Now.AddDays(-30), status, true, Now).State
            .Should().Be(AccountAccessState.Active, "an ended trial does not interrupt someone who pays");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Canceled)]
    [InlineData(SubscriptionStatus.Incomplete)]
    public void An_ended_stripe_subscription_is_Locked_even_with_trial_days_left(SubscriptionStatus status)
    {
        var access = AccountAccessPolicy.Resolve(Now.AddDays(5), status, hasStripeSubscription: true, Now);

        access.State.Should().Be(AccountAccessState.Locked, "canceling does not hand the unused trial back");
        access.LockReason.Should().Be(AccountLockReason.SubscriptionEnded);
    }

    [Fact]
    public void A_subscription_row_without_Stripe_is_not_paying()
    {
        // ★★ The old free plan's rows: Status = Active, no Stripe id.
        var access = AccountAccessPolicy.Resolve(Now.AddDays(-1), SubscriptionStatus.Active, hasStripeSubscription: false, Now);

        access.State.Should().Be(AccountAccessState.Locked);
        access.LockReason.Should().Be(AccountLockReason.TrialEnded);
    }

    [Fact]
    public void An_open_trial_is_Trial_and_counts_days_rounded_up()
    {
        var access = AccountAccessPolicy.Resolve(Now.AddHours(30), null, false, Now);

        access.State.Should().Be(AccountAccessState.Trial);
        access.TrialDaysRemaining.Should().Be(2, "30 hours left is shown as 2 days, never as less time than they have");
        AccountAccessPolicy.Resolve(Now.AddMinutes(5), null, false, Now).TrialDaysRemaining
            .Should().Be(1, "never 0 while still inside the trial");
    }

    [Fact]
    public void The_trial_ends_at_its_exact_instant()
    {
        AccountAccessPolicy.Resolve(Now, null, false, Now).State.Should().Be(AccountAccessState.Locked);
        AccountAccessPolicy.Resolve(Now.AddTicks(1), null, false, Now).State.Should().Be(AccountAccessState.Trial);
    }

    [Fact]
    public void No_trial_and_no_subscription_is_Locked_with_its_own_reason()
    {
        var access = AccountAccessPolicy.Resolve(null, null, false, Now);

        access.State.Should().Be(AccountAccessState.Locked);
        access.LockReason.Should().Be(AccountLockReason.NoSubscription, "never mistaken for an expired trial");
    }

    [Fact]
    public void A_trial_starts_once_and_cannot_be_restarted()
    {
        var tenant = Tenant.Create("Acme", "acme", Guid.NewGuid(), Now);
        tenant.StartTrial(Now.AddDays(7));

        var restart = () => tenant.StartTrial(Now.AddDays(30));

        restart.Should().Throw<DomainException>("a restartable trial is an unlimited free plan");
        tenant.TrialEndsAt.Should().Be(Now.AddDays(7), "the original end date is evidence and stays");
    }
}
