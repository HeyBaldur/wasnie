using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Exceptions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class SubscriptionEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    private static readonly IReadOnlyList<SubscriptionPlanDto> SamplePlans =
    [
        new(PriceId: "price_starter", ProductId: "prod_starter", Name: "Starter", Price: 29m,
            Currency: "EUR", Interval: "month", PlanCode: "starter",
            MaxPayees: 25, MaxPlans: 5, IsCurrentPlan: false),
        new(PriceId: "price_growth", ProductId: "prod_growth", Name: "Growth", Price: 79m,
            Currency: "EUR", Interval: "month", PlanCode: "growth",
            MaxPayees: 75, MaxPlans: 15, IsCurrentPlan: true),
        new(PriceId: "price_scale", ProductId: "prod_scale", Name: "Scale", Price: 199m,
            Currency: "EUR", Interval: "month", PlanCode: "scale",
            MaxPayees: 150, MaxPlans: -1, IsCurrentPlan: false),
    ];

    public SubscriptionEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Auth ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlans_WithoutToken_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var response = await anon.GetAsync("/api/subscription/plans");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConfig_WithoutToken_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var response = await anon.GetAsync("/api/subscription/config");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Plans endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlans_ReturnsThePaidPlansWithCorrectShape_AndNoFreePlan()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<ISubscriptionPlanService>(_ =>
                    new StubSubscriptionPlanService(SamplePlans))));

        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/subscription/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await response.Content.ReadFromJsonAsync<List<PlanResponse>>(JsonOptions);
        plans.Should().NotBeNull();
        plans!.Should().HaveCount(3);
        plans.Should().NotContain(p => p.PriceId == null, "KAN-77: there is no synthetic free plan any more");

        var starter = plans.First(p => p.PlanCode == "starter");
        starter.PriceId.Should().Be("price_starter");
        starter.Price.Should().Be(29m);
        starter.MaxPayees.Should().Be(25);

        var growth = plans.First(p => p.PlanCode == "growth");
        growth.IsCurrentPlan.Should().BeTrue();
    }

    [Fact]
    public async Task GetPlans_ResponseNeverContainsSecretKey()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<ISubscriptionPlanService>(_ =>
                    new StubSubscriptionPlanService(SamplePlans))));

        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/subscription/plans");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("sk_", because: "the Stripe secret key must never appear in any API response");
        body.Should().NotContain("secret", because: "no secret key word should appear in the plans response");
    }

    [Fact]
    public async Task GetPlans_WhenStripeIsDown_Returns503()
    {
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<ISubscriptionPlanService>(_ =>
                    new BrokenSubscriptionPlanService())));

        var client = factory.CreateClient().WithAuth(TestConstants.TenantA);
        var response = await client.GetAsync("/api/subscription/plans");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("temporarily unavailable");
    }

    // ── Config endpoint ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetConfig_ReturnsPublishableKey()
    {
        var response = await _client.GetAsync("/api/subscription/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ConfigResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.PublishableKey.Should().StartWith("pk_test_",
            because: "the endpoint returns the test publishable key");
    }

    [Fact]
    public async Task GetConfig_ResponseNeverContainsSecretKey()
    {
        var response = await _client.GetAsync("/api/subscription/config");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("sk_",
            because: "the Stripe secret key must never appear in the config response");
        body.Should().NotContain("secretKey",
            because: "secret key field name must not appear in the response");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed record PlanResponse(
        string? PriceId, string? ProductId, string Name, decimal Price,
        string Currency, string Interval, string PlanCode,
        int MaxPayees, int MaxPlans, bool IsCurrentPlan);

    private sealed record ConfigResponse(string PublishableKey);

    private sealed class StubSubscriptionPlanService(IReadOnlyList<SubscriptionPlanDto> plans) : ISubscriptionPlanService
    {
        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(string? currentPlanCode, CancellationToken cancellationToken = default)
            => Task.FromResult(plans);
    }

    private sealed class BrokenSubscriptionPlanService : ISubscriptionPlanService
    {
        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(string? currentPlanCode, CancellationToken cancellationToken = default)
            => throw new StripeUnavailableException("The subscription plan service is temporarily unavailable. Please try again shortly.");
    }
}
