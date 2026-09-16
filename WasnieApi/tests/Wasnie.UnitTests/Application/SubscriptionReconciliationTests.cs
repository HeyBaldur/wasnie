using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Application.Features.Subscription.Handlers;
using Wasnie.Application.Features.Subscription.Queries;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-77, critical: a customer paid €299 for a NEW Stripe subscription, no webhook arrived, and the database kept the
/// old cancelled one — the paywall locked out someone who had just paid. What must hold now: the live subscription is
/// the one followed; a locked account asks Stripe before staying locked; checkout never creates a second subscription.
/// </summary>
public sealed class SubscriptionReconciliationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IStripeSubscriptionReconciler _reconciler = Substitute.For<IStripeSubscriptionReconciler>();

    public SubscriptionReconciliationTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _tenant.IsResolved.Returns(true);
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            _tenant,
            Substitute.For<MediatR.IPublisher>());
    }

    // ── Which subscription the account follows ───────────────────────────────────────

    [Fact]
    public void PickLive_TheNewPaidSubscriptionWins_OverTheOldCancelledOne()
    {
        var picked = StripeSubscriptionStatus.PickLive([
            ("sub_1TiWhf_old", "canceled", new DateTime(2026, 6, 15)),
            ("sub_1UFY6n_new", "active", new DateTime(2026, 9, 14)),
        ]);

        picked.Should().Be("sub_1UFY6n_new");
    }

    [Fact]
    public void PickLive_MostRecentLive_AndPastDueStillCounts()
    {
        StripeSubscriptionStatus.PickLive([
            ("a", "past_due", new DateTime(2026, 9, 1)),
            ("b", "active", new DateTime(2026, 8, 1)),
        ]).Should().Be("a");
    }

    [Fact]
    public void PickLive_NothingLive_IsNull() =>
        StripeSubscriptionStatus.PickLive([
            ("a", "canceled", new DateTime(2026, 9, 1)),
            ("b", "incomplete_expired", new DateTime(2026, 9, 2)),
            ("c", "incomplete", new DateTime(2026, 9, 3)),
        ]).Should().BeNull();

    // ── Access: a locked account asks Stripe first ───────────────────────────────────

    [Fact]
    public async Task Access_Locked_ReconcilesAndReturnsTheCorrectedState()
    {
        var reader = Substitute.For<IAccountAccessReader>();
        reader.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(
            new AccountAccess(AccountAccessState.Locked, AccountLockReason.SubscriptionEnded, null, null),
            new AccountAccess(AccountAccessState.Active, null, null, null));
        _reconciler.ReconcileAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await AccessHandler(reader).Handle(new GetAccountAccessQuery(), default);

        result.Value!.State.Should().Be("Active", "the customer paid; Stripe says so; the paywall must let them in");
        await _reconciler.Received(1).ReconcileAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Access_Locked_NothingInStripe_StaysLocked()
    {
        var reader = Substitute.For<IAccountAccessReader>();
        reader.GetAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new AccountAccess(AccountAccessState.Locked, AccountLockReason.TrialEnded, Now, 0));
        _reconciler.ReconcileAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await AccessHandler(reader).Handle(new GetAccountAccessQuery(), default);

        result.Value!.State.Should().Be("Locked");
        await reader.Received(1).GetAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Access_Active_NeverCallsStripe()
    {
        var reader = Substitute.For<IAccountAccessReader>();
        reader.GetAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new AccountAccess(AccountAccessState.Active, null, null, null));

        await AccessHandler(reader).Handle(new GetAccountAccessQuery(), default);

        await _reconciler.DidNotReceiveWithAnyArgs().ReconcileAsync(default);
    }

    // ── Checkout: never a second subscription ────────────────────────────────────────

    [Fact]
    public async Task Checkout_AlreadySubscribedAfterSync_IsRefused_WithoutCreatingASession()
    {
        await SeedTenantAsync();
        var checkout = Substitute.For<IStripeCheckoutService>();
        _reconciler.ReconcileAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            // The sync finds the paid subscription the webhook missed.
            var sub = UserSubscription.CreatePending(Guid.NewGuid(), TenantId, "t@t.io", Now);
            sub.UpdateFromStripe("pro", SubscriptionStatus.Active, "sub_new", "cus_1", "price_pro", "prod_pro",
                Now, Now.AddMonths(1), Now.AddMonths(1), Now);
            _db.UserSubscriptions.Add(sub);
            await _db.SaveChangesAsync();
            return true;
        });

        var result = await CheckoutHandler(checkout).Handle(new CreateCheckoutSessionCommand("price_pro"), default);

        result.Value!.Blocked.Should().BeTrue();
        result.Value.BlockedReason.Should().Be(CreateCheckoutSessionHandler.AlreadySubscribedReason);
        await checkout.DidNotReceiveWithAnyArgs().CreateCheckoutSessionAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Checkout_CancelledSubscription_CreatesTheSession()
    {
        await SeedTenantAsync();
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), TenantId, "t@t.io", Now);
        sub.UpdateFromStripe("pro", SubscriptionStatus.Active, "sub_old", "cus_1", "price_pro", "prod_pro",
            Now, Now.AddMonths(1), Now.AddMonths(1), Now);
        sub.Cancel(Now);
        _db.UserSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var checkout = Substitute.For<IStripeCheckoutService>();
        checkout.CreateCheckoutSessionAsync(default, default!, default!, default).ReturnsForAnyArgs("https://checkout");

        var result = await CheckoutHandler(checkout).Handle(new CreateCheckoutSessionCommand("price_pro"), default);

        result.Value!.Blocked.Should().BeFalse();
        result.Value.CheckoutUrl.Should().Be("https://checkout");
        await _reconciler.Received(1).ReconcileAsync(Arg.Any<CancellationToken>());
    }

    // ── Billing: an ended stored subscription looks for a newer one ──────────────────

    [Fact]
    public async Task Billing_StoredSubscriptionEnded_Reconciles_AndReadsTheNewSubscription()
    {
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), TenantId, "t@t.io", Now);
        sub.UpdateFromStripe("pro", SubscriptionStatus.Active, "sub_old", "cus_1", "price_pro", "prod_pro",
            Now.AddMonths(-2), Now.AddMonths(-1), Now.AddMonths(-1), Now);
        sub.Cancel(Now);
        _db.UserSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var stripe = Substitute.For<IStripeBillingDetailsReader>();
        stripe.GetAsync("cus_1", "sub_old", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new StripeBillingDetails(
                new StripeSubscriptionSnapshot("canceled", null, null, false, null, Now.UtcDateTime), null, []));
        stripe.GetAsync("cus_1", "sub_new", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new StripeBillingDetails(
                new StripeSubscriptionSnapshot("active", Now.UtcDateTime, Now.AddMonths(1).UtcDateTime, false, null, null), null, []));

        _reconciler.ReconcileAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var row = await _db.UserSubscriptions.FirstAsync();
            row.UpdateFromStripe("pro", SubscriptionStatus.Active, "sub_new", "cus_1", "price_pro", "prod_pro",
                Now, Now.AddMonths(1), Now.AddMonths(1), Now);
            await _db.SaveChangesAsync();
            return true;
        });

        var result = await new GetBillingDetailsHandler(
                _db, _tenant, Substitute.For<IAuthorizationService>(), stripe, _reconciler,
                Substitute.For<ILogger<GetBillingDetailsHandler>>())
            .Handle(new GetBillingDetailsQuery(), default);

        result.Value!.Synced.Should().BeTrue();
        result.Value.Subscription!.Status.Should().Be("active");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private GetAccountAccessHandler AccessHandler(IAccountAccessReader reader)
    {
        var options = Options.Create(new BillingOptions());
        // The real balance reader, so the access response carries the same numbers the assistant's gate reads (KAN-83).
        return new(_db, _tenant, reader,
            new Wasnie.Application.Assistant.Common.AssistantTokenBalanceReader(_db, reader, options, new FakeClock()),
            _reconciler, options);
    }

    private CreateCheckoutSessionHandler CheckoutHandler(IStripeCheckoutService checkout)
    {
        var planService = Substitute.For<ISubscriptionPlanService>();
        planService.GetPlansAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            [new SubscriptionPlanDto("price_pro", "prod_pro", "Incentra Pro", 299m, "eur", "month", "pro", -1, -1, false)]);
        var user = Substitute.For<ICurrentUserService>();
        user.Email.Returns("t@t.io");
        return new CreateCheckoutSessionHandler(
            _db, _tenant, user, planService, TestPlanCatalog.Create(), checkout, _reconciler,
            Substitute.For<ILogger<CreateCheckoutSessionHandler>>());
    }

    private async Task SeedTenantAsync()
    {
        _db.Tenants.Add(Tenant.Create("T", "t-slug", TenantId, Now.UtcDateTime));
        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();
}
