using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class SubscriptionEnforcementTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;

    public SubscriptionEnforcementTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {TestConstants.TenantA}");

        var now = DateTimeOffset.UtcNow;
        var sub = UserSubscription.CreateFree(Guid.NewGuid(), TestConstants.TenantA, "test@wasnie.io", now);
        sub.UpdateFromStripe(
            tier: Tier.Growth,
            status: SubscriptionStatus.Active,
            stripeSubscriptionId: "sub_enforce_test",
            stripeCustomerId: "cus_enforce_test",
            stripePriceId: "price_growth",
            stripeProductId: "prod_growth",
            periodStart: now,
            periodEnd: now.AddMonths(1),
            nextBillingDate: now.AddMonths(1),
            now: now);
        sub.Cancel(now);
        db.UserSubscriptions.Add(sub);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {TestConstants.TenantA}");
    }

    // ── Functional endpoints blocked ─────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/payees")]
    [InlineData("GET", "/api/plans")]
    [InlineData("GET", "/api/transactions")]
    [InlineData("GET", "/api/credits")]
    [InlineData("GET", "/api/quotas")]
    [InlineData("GET", "/api/assignments")]
    public async Task FunctionalEndpoint_CanceledTenant_Returns402(string method, string path)
    {
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);

        var response = method == "GET"
            ? await client.GetAsync(path)
            : await client.PostAsJsonAsync(path, new { });

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task FunctionalEndpoint_CanceledTenant_ResponseBody_HasCode()
    {
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/payees");

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.Code.Should().Be("subscription_canceled");
    }

    // ── Exempt endpoints accessible ───────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentSubscription_CanceledTenant_Returns200()
    {
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/subscription/current");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPlans_CanceledTenant_Returns200()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<ISubscriptionPlanService>(_ => new StubPlanService())));
        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/subscription/plans");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetConfig_CanceledTenant_Returns200()
    {
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/subscription/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Non-canceled tenants not affected ────────────────────────────────────────

    [Fact]
    public async Task FunctionalEndpoint_Unauthenticated_Returns401_NotPaymentRequired()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/payees");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FunctionalEndpoint_TenantWithNoSubscription_NotBlocked()
    {
        // TenantB has no UserSubscription row — enforcement skips (null status)
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
        var response = await client.GetAsync("/api/payees");
        response.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
    }

    // ── Multi-tenant: TenantB not blocked by TenantA's Canceled status ───────────

    [Fact]
    public async Task FunctionalEndpoint_DifferentTenant_NotAffectedByCanceledTenantA()
    {
        var clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
        var response = await clientB.GetAsync("/api/payees");
        response.StatusCode.Should().NotBe(HttpStatusCode.PaymentRequired);
    }

    private sealed record ErrorBody(string Code, string Message);

    private sealed class StubPlanService : ISubscriptionPlanService
    {
        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(
            Tier currentTier, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SubscriptionPlanDto> plans =
            [
                new(PriceId: "price_starter", ProductId: "prod_starter", Name: "Starter",
                    Price: 29m, Currency: "EUR", Interval: "month", Tier: "Starter",
                    MaxPayees: 25, MaxPlans: 5, IsCurrentPlan: currentTier == Tier.Starter),
                new(PriceId: "price_growth", ProductId: "prod_growth", Name: "Growth",
                    Price: 79m, Currency: "EUR", Interval: "month", Tier: "Growth",
                    MaxPayees: 75, MaxPlans: 15, IsCurrentPlan: currentTier == Tier.Growth),
            ];
            return Task.FromResult(plans);
        }
    }
}
