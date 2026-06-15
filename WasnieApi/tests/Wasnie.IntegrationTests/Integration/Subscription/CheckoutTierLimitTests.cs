using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class CheckoutTierLimitTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private List<Payee> _seededPayees = [];

    public CheckoutTierLimitTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetPayeesAsync();

        // Seed a Canceled Scale subscription for TenantA — simulates a reactivating tenant.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = TestConstants.TenantA;

        await db.Database.ExecuteSqlAsync($"DELETE FROM UserSubscriptions WHERE TenantId = {tid}");

        var now = DateTimeOffset.UtcNow;
        var sub = UserSubscription.CreateFree(Guid.NewGuid(), tid, "test@wasnie.io", now);
        sub.UpdateFromStripe(
            tier: Tier.Scale,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_test_chk_limit",
            stripeCustomerId: "cus_test_chk_limit",
            stripePriceId: "price_scale",
            stripeProductId: "prod_scale",
            periodStart: now,
            periodEnd: now.AddMonths(1),
            nextBillingDate: null,
            now: now);
        sub.Cancel(now);
        db.UserSubscriptions.Add(sub);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tid = TestConstants.TenantA;
        await db.Database.ExecuteSqlAsync($"DELETE FROM UserSubscriptions WHERE TenantId = {tid}");
        if (_seededPayees.Count > 0)
        {
            db.Payees.RemoveRange(_seededPayees);
            await db.SaveChangesAsync();
        }
    }

    // ── Blocked: payee usage exceeds target tier ─────────────────────────────────

    [Fact]
    public async Task Checkout_WhenPayeesExceedStarterLimit_Returns409Blocked()
    {
        await SeedPayeesAsync(26); // Starter allows 25

        using var factory = CreateFactory();
        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/checkout", new { priceId = "price_starter" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<CheckoutResultDto>(JsonOptions);
        body!.Blocked.Should().BeTrue();
        body.BlockedReason.Should().Be("payees");
        body.Current.Should().Be(26);
        body.Limit.Should().Be(25);
        body.TargetTier.Should().Be("Starter");
    }

    [Fact]
    public async Task Checkout_WhenPayeesExceedGrowthLimit_Returns409Blocked()
    {
        await SeedPayeesAsync(76); // Growth allows 75

        using var factory = CreateFactory();
        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/checkout", new { priceId = "price_growth" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<CheckoutResultDto>(JsonOptions);
        body!.Blocked.Should().BeTrue();
        body.BlockedReason.Should().Be("payees");
        body.Current.Should().Be(76);
        body.Limit.Should().Be(75);
        body.TargetTier.Should().Be("Growth");
    }

    // ── Allowed: usage fits target tier ──────────────────────────────────────────

    [Fact]
    public async Task Checkout_WhenUsageFitsTargetTier_Returns200WithCheckoutUrl()
    {
        // 0 payees — fits any tier.
        using var factory = CreateFactory();
        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/checkout", new { priceId = "price_starter" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CheckoutSessionDto>(JsonOptions);
        body!.CheckoutUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Checkout_WhenPayees26AndTargetIsScale_Returns200()
    {
        // Scale allows 150 — 26 payees should not be blocked.
        await SeedPayeesAsync(26);

        using var factory = CreateFactory();
        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.PostAsJsonAsync("/api/subscription/checkout", new { priceId = "price_scale" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Multi-tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_TenantBNotBlockedByTenantAPayees()
    {
        // Seed 26 payees for TenantA (above Starter limit), but TenantB has 0.
        await SeedPayeesAsync(26);

        using var factory = CreateFactory();
        var tenantBClient = factory.CreateClient().WithAuth(TestConstants.TenantB);
        var response = await tenantBClient.PostAsJsonAsync("/api/subscription/checkout", new { priceId = "price_starter" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory() =>
        _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ISubscriptionPlanService>(_ => new StubPlanService());
                services.AddScoped<IStripeCheckoutService>(_ => new StubCheckoutService());
            }));

    private async Task SeedPayeesAsync(int count)
    {
        await _fixture.ResetPayeesAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        _seededPayees = Enumerable.Range(0, count).Select(i => Payee.Create(
            tenantId: TestConstants.TenantA,
            fullName: $"Payee {i} ChkLimit",
            employeeCode: $"CHKL{i:D3}",
            email: $"chklimit{i}@test.io",
            hireDate: null,
            createdBy: "test",
            id: Guid.NewGuid(),
            now: now)).ToList();
        db.Payees.AddRange(_seededPayees);
        await db.SaveChangesAsync();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class StubPlanService : ISubscriptionPlanService
    {
        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(
            Tier currentTier, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SubscriptionPlanDto> plans =
            [
                new(PriceId: "price_starter", ProductId: "prod_starter", Name: "Starter",
                    Price: 29m, Currency: "EUR", Interval: "month", Tier: "Starter",
                    MaxPayees: 25, MaxPlans: 5, IsCurrentPlan: false),
                new(PriceId: "price_growth", ProductId: "prod_growth", Name: "Growth",
                    Price: 79m, Currency: "EUR", Interval: "month", Tier: "Growth",
                    MaxPayees: 75, MaxPlans: 15, IsCurrentPlan: false),
                new(PriceId: "price_scale", ProductId: "prod_scale", Name: "Scale",
                    Price: 199m, Currency: "EUR", Interval: "month", Tier: "Scale",
                    MaxPayees: 150, MaxPlans: int.MaxValue, IsCurrentPlan: false),
            ];
            return Task.FromResult(plans);
        }
    }

    private sealed class StubCheckoutService : IStripeCheckoutService
    {
        public Task<string> CreateCheckoutSessionAsync(
            Guid tenantId, string priceId, string billingEmail,
            CancellationToken cancellationToken = default)
            => Task.FromResult("https://checkout.stripe.com/stub-session");
    }
}
