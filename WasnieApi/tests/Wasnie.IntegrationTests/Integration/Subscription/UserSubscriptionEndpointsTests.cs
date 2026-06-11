using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class UserSubscriptionEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public UserSubscriptionEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);

        // Clean up subscription rows and reset onboarding flag for test isolation.
        // Does NOT reset Tier — test tenants stay at Enterprise so capacity limits don't break other tests.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tidA = TestConstants.TenantA;
        var tidB = TestConstants.TenantB;
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {tidA} OR TenantId = {tidB}");
        // Restore Tier to Enterprise (value 4) because select-free sets Tier = Free,
        // and other integration tests rely on Enterprise tier to bypass capacity limits.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE Tenants SET HasSelectedPlan = 0, Tier = 4 WHERE Id = {tidA} OR Id = {tidB}");
    }

    public async Task DisposeAsync()
    {
        // Restore tenant state after each test so downstream test classes see Enterprise tier.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tidA = TestConstants.TenantA;
        var tidB = TestConstants.TenantB;
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {tidA} OR TenantId = {tidB}");
        await db.Database.ExecuteSqlAsync(
            $"UPDATE Tenants SET HasSelectedPlan = 0, Tier = 4 WHERE Id = {tidA} OR Id = {tidB}");
    }

    // ── Auth gates ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectFree_WithoutToken_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var response = await anon.PostAsync("/api/subscription/select-free", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrent_WithoutToken_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var response = await anon.GetAsync("/api/subscription/current");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── select-free creates UserSubscription row ───────────────────────────────

    [Fact]
    public async Task SelectFree_CreatesUserSubscriptionRow_WithCorrectFields()
    {
        var response = await _clientA.PostAsync("/api/subscription/select-free", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sub = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == TestConstants.TenantA);

        sub.Should().NotBeNull();
        sub!.Tier.ToString().Should().Be("Free");
        sub.Status.ToString().Should().Be("Active");
        sub.StripeSubscriptionId.Should().BeNull();
        sub.StripeCustomerId.Should().BeNull();
        sub.CurrentPeriodStart.Should().BeNull();
        sub.CurrentPeriodEnd.Should().BeNull();
    }

    // ── select-free sets HasSelectedPlan on Tenant ─────────────────────────────

    [Fact]
    public async Task SelectFree_SetsTenantHasSelectedPlan()
    {
        await _clientA.PostAsync("/api/subscription/select-free", null);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.FindAsync(TestConstants.TenantA);

        tenant!.HasSelectedPlan.Should().BeTrue();
        tenant.Tier.ToString().Should().Be("Free");
    }

    // ── select-free is idempotent ──────────────────────────────────────────────

    [Fact]
    public async Task SelectFree_IsIdempotent_DoesNotDuplicateRow()
    {
        await _clientA.PostAsync("/api/subscription/select-free", null);
        var response2 = await _clientA.PostAsync("/api/subscription/select-free", null);

        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var count = await db.UserSubscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == TestConstants.TenantA);

        count.Should().Be(1);
    }

    // ── GET /api/subscription/current returns the subscription ─────────────────

    [Fact]
    public async Task GetCurrent_AfterSelectFree_ReturnsCorrectSubscription()
    {
        await _clientA.PostAsync("/api/subscription/select-free", null);

        var response = await _clientA.GetAsync("/api/subscription/current");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sub = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(JsonOptions);
        sub.Should().NotBeNull();
        sub!.Tier.Should().Be("Free");
        sub.Status.Should().Be("Active");
        sub.StripeSubscriptionId.Should().BeNull();
        sub.CurrentPeriodStart.Should().BeNull();
    }

    // ── GET /api/subscription/current returns 404 if no subscription yet ────────

    [Fact]
    public async Task GetCurrent_BeforeSelectFree_Returns404()
    {
        var response = await _clientA.GetAsync("/api/subscription/current");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Multi-tenant isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task SelectFree_IsTenantScoped_TenantBCannotSeeTenantASubscription()
    {
        await _clientA.PostAsync("/api/subscription/select-free", null);

        var responseB = await _clientB.GetAsync("/api/subscription/current");
        responseB.StatusCode.Should().Be(HttpStatusCode.NotFound,
            because: "TenantB has not selected a plan; TenantA's subscription is not visible");
    }

    // ── Guard scenario: close wizard without selecting → wizard again ────────────

    [Fact]
    public async Task GetCurrentUser_BeforeSelectFree_HasSelectedPlanIsFalse()
    {
        var response = await _clientA.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        me!.HasSelectedPlan.Should().BeFalse(
            because: "the tenant has not yet selected a plan");
    }

    [Fact]
    public async Task GetCurrentUser_AfterSelectFree_HasSelectedPlanIsTrue()
    {
        await _clientA.PostAsync("/api/subscription/select-free", null);

        var response = await _clientA.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        me!.HasSelectedPlan.Should().BeTrue(
            because: "the tenant has selected the Free plan");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed record SubscriptionResponse(
        string Tier, string Status, string BillingEmail,
        string? StripeSubscriptionId, string? StripeCustomerId,
        string? StripePriceId, string? StripeProductId,
        DateTimeOffset? CurrentPeriodStart, DateTimeOffset? CurrentPeriodEnd,
        DateTimeOffset? NextBillingDate, DateTimeOffset? CanceledAt,
        DateTimeOffset CreatedAt);

    private sealed record MeResponse(bool HasSelectedPlan, string Tier);
}
