using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// The gate that decides whether a tenant may use the metered capabilities (AI assistant, HubSpot).
///
/// KAN-77 changed the question from "is the tier paid?" to "does the ACCOUNT have access?": a trial gets the
/// full product, a paying account too, a locked one does not. Pinned here, both costing money if they break:
/// the state is read from the DATABASE (a cancellation takes effect immediately, not when a token expires),
/// and anything short of proven access is a refusal.
/// </summary>
public sealed class PaidPlanGateTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(PaidPlanGate Gate, ApplicationDbContext Db, Guid TenantId, FakeClock Clock);

    private static ApplicationDbContext NewDb(ITenantContext? ctx = null) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"paid-plan-gate-{Guid.NewGuid()}")
                .Options,
            ctx ?? Substitute.For<ITenantContext>(), Substitute.For<MediatR.IPublisher>());

    private static Harness Build(DateTimeOffset? trialEndsAt, SubscriptionStatus? status = null, bool stripe = false)
    {
        var tenantId = Guid.NewGuid();
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);

        var db = NewDb(ctx);
        var clock = new FakeClock(Now);

        var tenant = Tenant.Create("Tenant", $"t-{tenantId:N}", tenantId, clock.UtcNowOffset);
        if (trialEndsAt is not null) tenant.StartTrial(trialEndsAt.Value);
        db.Tenants.Add(tenant);

        if (status is not null)
        {
            var sub = UserSubscription.CreatePending(Guid.NewGuid(), tenantId, "billing@test", clock.UtcNowOffset);
            if (stripe)
                sub.UpdateFromStripe("pro", status.Value, "sub_1", "cus_1", "price_1", "prod_1",
                    null, null, null, clock.UtcNowOffset);
            else if (status == SubscriptionStatus.Active)
                sub.Recover(clock.UtcNowOffset); // the legacy free row: Active, no Stripe id
            db.UserSubscriptions.Add(sub);
        }
        db.SaveChanges();

        var reader = new AccountAccessReader(db, clock);
        return new Harness(new PaidPlanGate(ctx, reader, TestPlanCatalog.Create()), db, tenantId, clock);
    }

    [Fact]
    public async Task A_trial_gets_the_metered_features_too()
    {
        // Product decision in KAN-77: the trial is the full product, assistant and HubSpot included.
        var h = Build(trialEndsAt: new DateTimeOffset(Now.AddDays(5)));

        (await h.Gate.IsOnPaidPlanAsync()).Should().BeTrue();
        await h.Gate.Invoking(g => g.RequirePaidPlanAsync("HubSpot")).Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    public async Task A_live_stripe_subscription_passes(SubscriptionStatus status)
    {
        var h = Build(trialEndsAt: null, status, stripe: true);

        (await h.Gate.IsOnPaidPlanAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task An_ended_trial_is_refused()
    {
        var h = Build(trialEndsAt: new DateTimeOffset(Now.AddMinutes(-1)));

        (await h.Gate.IsOnPaidPlanAsync()).Should().BeFalse();
        await h.Gate.Invoking(g => g.RequirePaidPlanAsync("HubSpot")).Should().ThrowAsync<PaidPlanRequiredException>();
    }

    [Fact]
    public async Task A_canceled_subscription_is_refused_even_with_trial_days_left()
    {
        // ★ Once they subscribed, the trial no longer counts: canceling does not hand the unused days back.
        var h = Build(trialEndsAt: new DateTimeOffset(Now.AddDays(5)), SubscriptionStatus.Canceled, stripe: true);

        (await h.Gate.IsOnPaidPlanAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_former_free_plan_row_marked_Active_is_NOT_paying()
    {
        // ★★ The old free plan wrote UserSubscriptions with Status = Active and no Stripe id. If that counted as
        // paying, every former Free tenant would skip the paywall the day their trial ends.
        var h = Build(trialEndsAt: new DateTimeOffset(Now.AddDays(-1)), SubscriptionStatus.Active, stripe: false);

        (await h.Gate.IsOnPaidPlanAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task The_trial_ending_takes_effect_on_the_very_next_call()
    {
        // ★ Read fresh, never cached: the moment the trial ends the metered features stop, whatever token is live.
        var h = Build(trialEndsAt: new DateTimeOffset(Now.AddHours(1)));
        (await h.Gate.IsOnPaidPlanAsync()).Should().BeTrue();

        h.Clock.Advance(TimeSpan.FromHours(2));

        (await h.Gate.IsOnPaidPlanAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task An_unresolved_tenant_is_refused_rather_than_assumed_paid()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var db = NewDb();

        var gate = new PaidPlanGate(ctx, new AccountAccessReader(db, new FakeClock(Now)), TestPlanCatalog.Create());

        (await gate.IsOnPaidPlanAsync()).Should().BeFalse("\"we don't know\" must never spend money");
        await gate.Invoking(g => g.RequirePaidPlanAsync("HubSpot"))
            .Should().ThrowAsync<PaidPlanRequiredException>();
    }

    [Fact]
    public async Task A_tenant_row_that_does_not_exist_is_refused()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(Guid.NewGuid());
        ctx.IsResolved.Returns(true);
        var db = NewDb();

        var gate = new PaidPlanGate(ctx, new AccountAccessReader(db, new FakeClock(Now)), TestPlanCatalog.Create());

        (await gate.IsOnPaidPlanAsync()).Should().BeFalse();
    }
}
