using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;
using Stripe;

namespace Wasnie.IntegrationTests.Integration.Subscription;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class ChangePlanEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    public ChangePlanEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);

        await _fixture.ResetPayeesAsync();

        // Seed an active Growth subscription so we can test upgrade/downgrade.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = TestConstants.TenantA;

        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {tid}");

        var now = DateTimeOffset.UtcNow;
        var sub = UserSubscription.CreatePending(Guid.NewGuid(), tid, "test@wasnie.io", now);
        sub.UpdateFromStripe(
            planCode: "pro",
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_test_change",
            stripeCustomerId: "cus_test_change",
            stripePriceId: "price_growth",
            stripeProductId: "prod_growth",
            periodStart: now,
            periodEnd: now.AddMonths(1),
            nextBillingDate: now.AddMonths(1),
            now: now);
        db.UserSubscriptions.Add(sub);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = TestConstants.TenantA;
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {tid}");
    }

    // ── Auth ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePlan_WithoutToken_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var response = await anon.PostAsJsonAsync("/api/subscription/change-plan", new { targetPlanCode = "scale" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BillingPortal_WithoutToken_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var response = await anon.PostAsync("/api/subscription/billing-portal", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Invalid tier ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Free")]
    [InlineData("Enterprise")]
    [InlineData("invalid")]
    [InlineData("")]
    public async Task ChangePlan_InvalidTargetTier_Returns400(string tier)
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ISubscriptionPlanService>(_ => new StubPlanService());
                services.AddSingleton<Wasnie.Application.Features.Subscription.ISubscriptionPlanCatalog>(TestPlans.ThreePlanCatalog());
                services.AddScoped<IStripeSubscriptionManagementService>(_ => new StubStripeManagement());
            }));

        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/change-plan", new { targetPlanCode = tier });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Downgrade blocked by payee count ────────────────────────────────────────

    [Fact]
    public async Task ChangePlan_DowngradeBlockedByPayeeCount_Returns409WithBlockedPayload()
    {
        // Tenant is on Growth. Starter allows 25 payees.
        // We seed 26 payees so the downgrade to Starter is blocked.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = TestConstants.TenantA;

        // Seed 26 payees (beyond Starter limit of 25)
        var now = DateTimeOffset.UtcNow;
        var payees = Enumerable.Range(0, 26).Select(i => Payee.Create(
            tenantId: tid,
            fullName: $"Payee {i} Test",
            employeeCode: $"EMP{i:D3}",
            email: $"payee{i}@test.io",
            hireDate: null,
            createdBy: "test",
            id: Guid.NewGuid(),
            now: now)).ToList();
        db.Payees.AddRange(payees);
        await db.SaveChangesAsync();

        try
        {
            using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddScoped<ISubscriptionPlanService>(_ => new StubPlanService());
                    services.AddSingleton<Wasnie.Application.Features.Subscription.ISubscriptionPlanCatalog>(TestPlans.ThreePlanCatalog());
                    services.AddScoped<IStripeSubscriptionManagementService>(_ => new StubStripeManagement());
                }));

            var client = factory.CreateClient().WithAuth(tid);
            var response = await client.PostAsJsonAsync("/api/subscription/change-plan",
                new { targetPlanCode = "starter" });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await response.Content.ReadFromJsonAsync<ChangePlanResultDto>(JsonOptions);
            body.Should().NotBeNull();
            body!.Blocked.Should().BeTrue();
            body.BlockedReason.Should().Be("payees");
            body.Current.Should().Be(26);
            body.Limit.Should().Be(25);
            body.TargetPlanCode.Should().Be("starter");
        }
        finally
        {
            db.Payees.RemoveRange(payees);
            await db.SaveChangesAsync();
        }
    }

    // ── Successful plan change (mocked Stripe) ──────────────────────────────────

    [Fact]
    public async Task ChangePlan_ValidUpgrade_Returns200Pending()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ISubscriptionPlanService>(_ => new StubPlanService());
                services.AddSingleton<Wasnie.Application.Features.Subscription.ISubscriptionPlanCatalog>(TestPlans.ThreePlanCatalog());
                services.AddScoped<IStripeSubscriptionManagementService>(_ => new StubStripeManagement());
            }));

        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/change-plan",
            new { targetPlanCode = "scale" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChangePlanResultDto>(JsonOptions);
        body.Should().NotBeNull();
        body!.Pending.Should().BeTrue();
        body.Blocked.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePlan_ValidDowngrade_Returns200Pending()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ISubscriptionPlanService>(_ => new StubPlanService());
                services.AddSingleton<Wasnie.Application.Features.Subscription.ISubscriptionPlanCatalog>(TestPlans.ThreePlanCatalog());
                services.AddScoped<IStripeSubscriptionManagementService>(_ => new StubStripeManagement());
            }));

        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/change-plan",
            new { targetPlanCode = "starter" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChangePlanResultDto>(JsonOptions);
        body.Should().NotBeNull();
        body!.Pending.Should().BeTrue();
        body.Blocked.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePlan_UpgradePaymentFailed_Returns400WithUpgradePaymentFailedCode()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ISubscriptionPlanService>(_ => new StubPlanService());
                services.AddSingleton<Wasnie.Application.Features.Subscription.ISubscriptionPlanCatalog>(TestPlans.ThreePlanCatalog());
                services.AddScoped<IStripeSubscriptionManagementService>(_ => new StubStripeManagementUpgradeFails());
            }));

        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/change-plan",
            new { targetPlanCode = "scale" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("upgrade_payment_failed");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class StubPlanService : ISubscriptionPlanService
    {
        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(
            string? currentPlanCode, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SubscriptionPlanDto> plans =
            [
                new(PriceId: "price_starter", ProductId: "prod_starter", Name: "Starter",
                    Price: 29m, Currency: "EUR", Interval: "month", PlanCode: "starter",
                    MaxPayees: 25, MaxPlans: 5, IsCurrentPlan: currentPlanCode == "starter"),
                new(PriceId: "price_growth", ProductId: "prod_growth", Name: "Growth",
                    Price: 79m, Currency: "EUR", Interval: "month", PlanCode: "growth",
                    MaxPayees: 75, MaxPlans: 15, IsCurrentPlan: currentPlanCode == "growth"),
                new(PriceId: "price_scale", ProductId: "prod_scale", Name: "Scale",
                    Price: 199m, Currency: "EUR", Interval: "month", PlanCode: "scale",
                    MaxPayees: 150, MaxPlans: -1, IsCurrentPlan: currentPlanCode == "scale"),
            ];
            return Task.FromResult(plans);
        }
    }

    private sealed class StubStripeManagement : IStripeSubscriptionManagementService
    {
        // Stripe says the subscription is on "growth" (the DB row is seeded as "pro" and gets synced).
        public Task<string?> GetCurrentPlanCodeFromStripeAsync(string subscriptionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("growth");

        public Task UpgradeSubscriptionAsync(string subscriptionId, string newPriceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult("https://billing.stripe.com/test-portal");

        public Task RevertCancellationAsync(string subscriptionId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubStripeManagementUpgradeFails : IStripeSubscriptionManagementService
    {
        public Task<string?> GetCurrentPlanCodeFromStripeAsync(string subscriptionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("growth");

        public Task UpgradeSubscriptionAsync(string subscriptionId, string newPriceId,
            CancellationToken cancellationToken = default)
            => Task.FromException(new Stripe.StripeException("Your card was declined."));

        public Task UpdateSubscriptionAsync(string subscriptionId, string newPriceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult("https://billing.stripe.com/test-portal");

        public Task RevertCancellationAsync(string subscriptionId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
