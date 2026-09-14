using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Stripe;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Application.Features.Subscription.Handlers;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// KAN-77: plans are codes from the catalog, not the Starter/Growth/Scale enum. The shipped catalog has one plan,
/// so these tests run against a THREE-plan catalog — the future the multi-plan infrastructure exists for — to keep
/// the money semantics pinned: upgrade charges now, downgrade prorates, a stale DB never picks the wrong branch.
/// </summary>
public sealed class ChangePlanCommandHandlerTests : IDisposable
{
    private static readonly Wasnie.Application.Features.Subscription.SubscriptionPlanCatalog Catalog = TestPlanCatalog.Create(
        new SubscriptionPlanDefinition { Code = "starter", MaxPayees = 25, MaxPlans = 5 },
        new SubscriptionPlanDefinition { Code = "growth", MaxPayees = 75, MaxPlans = 15 },
        new SubscriptionPlanDefinition { Code = "scale", MaxPayees = 150, MaxPlans = null });

    private static readonly DateTimeOffset Now = new(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantCtx;
    private readonly ISubscriptionPlanService _planService;
    private readonly IStripeSubscriptionManagementService _stripe;
    private readonly ILogger<ChangePlanCommandHandler> _logger;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ChangePlanCommandHandlerTests()
    {
        _tenantCtx = Substitute.For<ITenantContext>();
        _tenantCtx.TenantId.Returns(TenantId);
        _tenantCtx.IsResolved.Returns(true);

        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            _tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        _planService = Substitute.For<ISubscriptionPlanService>();
        _stripe = Substitute.For<IStripeSubscriptionManagementService>();
        _logger = Substitute.For<ILogger<ChangePlanCommandHandler>>();

        _clock = Substitute.For<IClock>();
        _clock.UtcNow.Returns(Now.UtcDateTime);
        _clock.UtcNowOffset.Returns(Now);

        _audit = Substitute.For<IAuditService>();
    }

    // ── Invalid tier ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Free")]
    [InlineData("Enterprise")]
    [InlineData("bogus")]
    [InlineData("")]
    public async Task Handle_InvalidTargetTier_ReturnsFailure(string tier)
    {
        var result = await Create().Handle(new ChangePlanCommand(tier), default);
        result.IsSuccess.Should().BeFalse();
    }

    // ── No subscription ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoSubscription_ReturnsFailure()
    {
        var result = await Create().Handle(new ChangePlanCommand("Scale"), default);
        result.IsSuccess.Should().BeFalse();
    }

    // ── Same tier ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SameTier_ReturnsSuccessNotPending()
    {
        await SeedSubscriptionAsync("growth");
        SetupStripeTierReturns("growth");

        var result = await Create().Handle(new ChangePlanCommand("Growth"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Pending.Should().BeFalse();
        result.Value.Blocked.Should().BeFalse();
    }

    // ── Upgrade ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Upgrade_CallsUpgradeSubscriptionAsync()
    {
        await SeedSubscriptionAsync("growth");
        SetupStripeTierReturns("growth");
        SetupPlans();

        await Create().Handle(new ChangePlanCommand("Scale"), default);

        await _stripe.Received(1).UpgradeSubscriptionAsync("sub_test", "price_scale", Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().UpdateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Upgrade_ReturnsPending()
    {
        await SeedSubscriptionAsync("growth");
        SetupStripeTierReturns("growth");
        SetupPlans();

        var result = await Create().Handle(new ChangePlanCommand("Scale"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Pending.Should().BeTrue();
        result.Value.Blocked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Upgrade_PaymentDeclined_ReturnsFailureWithUpgradeCode()
    {
        await SeedSubscriptionAsync("growth");
        SetupStripeTierReturns("growth");
        SetupPlans();
        _stripe.UpgradeSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new StripeException("Your card was declined."));

        var result = await Create().Handle(new ChangePlanCommand("Scale"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("upgrade_payment_failed");
    }

    [Fact]
    public async Task Handle_Upgrade_PaymentDeclined_DoesNotCallDowngrade()
    {
        await SeedSubscriptionAsync("growth");
        SetupStripeTierReturns("growth");
        SetupPlans();
        _stripe.UpgradeSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new StripeException("Your card was declined."));

        await Create().Handle(new ChangePlanCommand("Scale"), default);

        await _stripe.DidNotReceive().UpdateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Downgrade ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Downgrade_CallsUpdateSubscriptionAsync()
    {
        await SeedSubscriptionAsync("scale");
        SetupStripeTierReturns("scale");
        SetupPlans();

        await Create().Handle(new ChangePlanCommand("Growth"), default);

        await _stripe.Received(1).UpdateSubscriptionAsync("sub_test", "price_growth", Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().UpgradeSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Downgrade_ReturnsPending()
    {
        await SeedSubscriptionAsync("scale");
        SetupStripeTierReturns("scale");
        SetupPlans();

        var result = await Create().Handle(new ChangePlanCommand("Growth"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Pending.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DowngradeBlockedByPayees_ReturnsBlockedResult()
    {
        await SeedSubscriptionAsync("growth");
        SetupStripeTierReturns("growth");
        SetupPlans();
        // Starter allows 25 payees. Seed 26.
        var payees = Enumerable.Range(0, 26).Select(i => Payee.Create(
            tenantId: TenantId, fullName: $"P {i}", employeeCode: $"E{i:D3}",
            email: $"p{i}@t.io", hireDate: null, createdBy: "test",
            id: Guid.NewGuid(), now: Now)).ToList();
        _db.Payees.AddRange(payees);
        await _db.SaveChangesAsync();

        var result = await Create().Handle(new ChangePlanCommand("Starter"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Blocked.Should().BeTrue();
        result.Value.BlockedReason.Should().Be("payees");
        result.Value.Current.Should().Be(26);
        result.Value.Limit.Should().Be(25);
    }

    [Fact]
    public async Task Handle_DowngradeBlockedByPayees_DoesNotCallStripe()
    {
        await SeedSubscriptionAsync("growth");
        SetupStripeTierReturns("growth");
        SetupPlans();
        var payees = Enumerable.Range(0, 26).Select(i => Payee.Create(
            tenantId: TenantId, fullName: $"P {i}", employeeCode: $"E{i:D3}",
            email: $"p{i}@t.io", hireDate: null, createdBy: "test",
            id: Guid.NewGuid(), now: Now)).ToList();
        _db.Payees.AddRange(payees);
        await _db.SaveChangesAsync();

        await Create().Handle(new ChangePlanCommand("Starter"), default);

        await _stripe.DidNotReceive().UpdateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().UpgradeSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Stale DB tier — the core bug fix ──────────────────────────────────

    [Fact]
    public async Task Handle_UpgradeWithStaleDbTier_UsesStripeTierAndCallsUpgrade()
    {
        // DB says Scale (stale from a previous test), but Stripe says Starter.
        // Target is Growth. Without the fix this would be Growth(2) < Scale(3) → downgrade path.
        // With the fix it must be Growth(2) > Starter(1) → upgrade path → charges immediately.
        await SeedSubscriptionAsync("scale");
        SetupStripeTierReturns("starter");  // authoritative Stripe tier
        SetupPlans();

        await Create().Handle(new ChangePlanCommand("Growth"), default);

        await _stripe.Received(1).UpgradeSubscriptionAsync("sub_test", "price_growth", Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().UpdateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UpgradeWithStaleDbTier_SyncsDbTierBeforeDeciding()
    {
        // DB=Scale, Stripe=Starter → DB must be corrected to Starter before upgrade call.
        await SeedSubscriptionAsync("scale");
        SetupStripeTierReturns("starter");
        SetupPlans();

        await Create().Handle(new ChangePlanCommand("Growth"), default);

        var sub = await _db.UserSubscriptions.FirstAsync(s => s.TenantId == TenantId);
        sub.PlanCode.Should().Be("starter");
    }

    [Fact]
    public async Task Handle_ConsecutiveUpgrades_BothDecideByStripeTier()
    {
        // Simulates two rapid upgrades where the webhook from the first has not arrived yet.
        // First: DB=Starter, Stripe=Starter → Starter→Growth. Upgrade path. ✓
        // Second: DB still Starter (stale), Stripe=Growth (updated by Stripe) → Growth→Scale. Still upgrade. ✓
        await SeedSubscriptionAsync("starter");
        SetupStripeTierReturns("starter");
        SetupPlans();

        var result1 = await Create().Handle(new ChangePlanCommand("Growth"), default);

        result1.IsSuccess.Should().BeTrue();
        result1.Value!.Pending.Should().BeTrue();
        await _stripe.Received(1).UpgradeSubscriptionAsync("sub_test", "price_growth", Arg.Any<CancellationToken>());

        // Simulate Stripe now showing Growth (the subscription was updated) but DB still Starter.
        SetupStripeTierReturns("growth");

        var result2 = await Create().Handle(new ChangePlanCommand("Scale"), default);

        result2.IsSuccess.Should().BeTrue();
        result2.Value!.Pending.Should().BeTrue();
        await _stripe.Received(1).UpgradeSubscriptionAsync("sub_test", "price_scale", Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().UpdateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Stripe read failure — abort clean ─────────────────────────────────

    [Fact]
    public async Task Handle_StripeReadThrows_ReturnsFailureWithPlanChangeUnavailable()
    {
        await SeedSubscriptionAsync("starter");
        _stripe.GetCurrentPlanCodeFromStripeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new StripeException("Connection timeout"));

        var result = await Create().Handle(new ChangePlanCommand("Growth"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("plan_change_unavailable");
    }

    [Fact]
    public async Task Handle_StripeReadReturnsNull_ReturnsFailureWithPlanChangeUnavailable()
    {
        await SeedSubscriptionAsync("starter");
        _stripe.GetCurrentPlanCodeFromStripeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await Create().Handle(new ChangePlanCommand("Growth"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("plan_change_unavailable");
    }

    [Fact]
    public async Task Handle_StripeReadFails_DoesNotCallStripeUpdate()
    {
        await SeedSubscriptionAsync("starter");
        _stripe.GetCurrentPlanCodeFromStripeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new StripeException("API unavailable"));

        await Create().Handle(new ChangePlanCommand("Growth"), default);

        await _stripe.DidNotReceive().UpgradeSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().UpdateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Idempotency — multi-tenant isolation ───────────────────────────────

    [Fact]
    public async Task Handle_OtherTenantSubscription_ReturnsFailure()
    {
        // Subscription seeded for a DIFFERENT tenant — current tenant has none.
        var otherTenantId = Guid.NewGuid();
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), otherTenantId, "other@t.io", Now);
        sub.UpdateFromStripe("growth", SubscriptionStatus.Active, "sub_other", "cus_other",
            "price_growth", "prod_growth", Now, Now.AddMonths(1), Now.AddMonths(1), Now);
        _db.UserSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var result = await Create().Handle(new ChangePlanCommand("Scale"), default);

        result.IsSuccess.Should().BeFalse();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private ChangePlanCommandHandler Create() =>
        new(_db, _tenantCtx, _planService, Catalog, _stripe, _logger, _clock, _audit);

    private async Task SeedSubscriptionAsync(string plan)
    {
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), TenantId, "test@t.io", Now);
        sub.UpdateFromStripe(plan, SubscriptionStatus.Active, "sub_test", "cus_test",
            $"price_{plan}", $"prod_{plan}",
            Now, Now.AddMonths(1), Now.AddMonths(1), Now);
        _db.UserSubscriptions.Add(sub);
        await _db.SaveChangesAsync();
    }

    private void SetupStripeTierReturns(string plan)
    {
        _stripe.GetCurrentPlanCodeFromStripeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)plan);
    }

    private void SetupPlans()
    {
        IReadOnlyList<SubscriptionPlanDto> plans =
        [
            new("price_starter", "prod_starter", "Starter", 300m, "EUR", "month", "starter",
                25, 5, false),
            new("price_growth", "prod_growth", "Growth", 800m, "EUR", "month", "growth",
                75, 15, false),
            new("price_scale", "prod_scale", "Scale", 1800m, "EUR", "month", "scale",
                150, -1, false),
        ];
        _planService.GetPlansAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(plans);
    }

    public void Dispose() => _db.Dispose();
}
