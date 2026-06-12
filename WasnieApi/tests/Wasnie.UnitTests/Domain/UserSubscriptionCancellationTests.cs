using FluentAssertions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;

namespace Wasnie.UnitTests.Domain;

public sealed class UserSubscriptionCancellationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CancelAt = new(2026, 7, 11, 0, 0, 0, TimeSpan.Zero);

    private static UserSubscription MakeActivePaidSub()
    {
        var sub = UserSubscription.CreateFree(Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now);
        sub.UpdateFromStripe(
            tier: Tier.Scale,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_test",
            stripeCustomerId: "cus_test",
            stripePriceId: "price_scale",
            stripeProductId: "prod_scale",
            periodStart: Now,
            periodEnd: CancelAt,
            nextBillingDate: CancelAt,
            now: Now);
        return sub;
    }

    private static void ReactivateSub(UserSubscription sub)
    {
        var newPeriodStart = CancelAt.AddDays(1);
        var newPeriodEnd = newPeriodStart.AddMonths(1);
        sub.UpdateFromStripe(
            tier: Tier.Scale,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_reactivated",
            stripeCustomerId: "cus_test",
            stripePriceId: "price_scale",
            stripeProductId: "prod_scale",
            periodStart: newPeriodStart,
            periodEnd: newPeriodEnd,
            nextBillingDate: newPeriodEnd,
            now: Now);
    }

    // ── ScheduleCancellation ───────────────────────────────────────────────

    [Fact]
    public void ScheduleCancellation_SetsCancelAtPeriodEndTrue()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.CancelAtPeriodEnd.Should().BeTrue();
    }

    [Fact]
    public void ScheduleCancellation_SetsCancelAt()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.CancelAt.Should().Be(CancelAt);
    }

    [Fact]
    public void ScheduleCancellation_StatusRemainsActive()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void ScheduleCancellation_UpdatesUpdatedAt()
    {
        var later = Now.AddMinutes(5);
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, later);
        sub.UpdatedAt.Should().Be(later);
    }

    // ── ClearCancellationSchedule ──────────────────────────────────────────

    [Fact]
    public void ClearCancellationSchedule_SetsCancelAtPeriodEndFalse()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.ClearCancellationSchedule(Now);
        sub.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public void ClearCancellationSchedule_NullsCancelAt()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.ClearCancellationSchedule(Now);
        sub.CancelAt.Should().BeNull();
    }

    [Fact]
    public void ClearCancellationSchedule_StatusRemainsActive()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.ClearCancellationSchedule(Now);
        sub.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void ClearCancellationSchedule_WhenNotScheduled_IsIdempotent()
    {
        var sub = MakeActivePaidSub();
        sub.ClearCancellationSchedule(Now);
        sub.CancelAtPeriodEnd.Should().BeFalse();
        sub.CancelAt.Should().BeNull();
    }

    // ── UpdateFromStripe — cancellation fields always reset on activation ────────

    [Fact]
    public void UpdateFromStripe_WhenCancelScheduled_ClearsCancelAtPeriodEnd()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        ReactivateSub(sub);
        sub.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public void UpdateFromStripe_WhenCancelScheduled_ClearsCancelAt()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        ReactivateSub(sub);
        sub.CancelAt.Should().BeNull();
    }

    [Fact]
    public void UpdateFromStripe_WhenCanceled_ClearsCanceledAt()
    {
        var sub = MakeActivePaidSub();
        sub.Cancel(Now);
        ReactivateSub(sub);
        sub.CanceledAt.Should().BeNull();
    }

    [Fact]
    public void UpdateFromStripe_WhenCanceled_SetsStatusActive()
    {
        var sub = MakeActivePaidSub();
        sub.Cancel(Now);
        ReactivateSub(sub);
        sub.Status.Should().Be(SubscriptionStatus.Active);
    }

    // ── Recover — CanceledAt cleared (defensive invariant) ───────────────────────

    [Fact]
    public void Recover_ClearsCanceledAt()
    {
        var sub = MakeActivePaidSub();
        sub.Cancel(Now);
        sub.Recover(Now);
        sub.CanceledAt.Should().BeNull();
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_WhenScheduled_ClearsCancelAtPeriodEnd()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.Cancel(Now);
        sub.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public void Cancel_WhenScheduled_ClearsCancelAt()
    {
        var sub = MakeActivePaidSub();
        sub.ScheduleCancellation(CancelAt, Now);
        sub.Cancel(Now);
        sub.CancelAt.Should().BeNull();
    }
}
