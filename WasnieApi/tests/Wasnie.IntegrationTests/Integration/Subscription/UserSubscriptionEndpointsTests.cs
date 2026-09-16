using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Subscription;

/// <summary>
/// The subscription row and the current-user plan, after KAN-77. These tests used to drive
/// <c>POST /api/subscription/select-free</c>; the free plan is gone, so what they pin now is that the endpoint no
/// longer exists, that a trial tenant has no subscription and no plan, and that tenants stay isolated.
/// </summary>
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
        await ResetAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private async Task ResetAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tidA = TestConstants.TenantA;
        var tidB = TestConstants.TenantB;
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM UserSubscriptions WHERE TenantId = {tidA} OR TenantId = {tidB}");
        await db.Database.ExecuteSqlAsync(
            $"UPDATE Tenants SET HasSelectedPlan = 0, PlanCode = NULL WHERE Id = {tidA} OR Id = {tidB}");
    }

    [Fact]
    public async Task SelectFree_no_longer_exists()
    {
        var response = await _clientA.PostAsync("/api/subscription/select-free", null);

        response.IsSuccessStatusCode.Should().BeFalse("KAN-77 removed the free plan and its endpoint");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.UserSubscriptions.IgnoreQueryFilters().AnyAsync(s => s.TenantId == TestConstants.TenantA))
            .Should().BeFalse("nothing may create a subscription row without Stripe any more");
    }

    [Fact]
    public async Task GetCurrent_for_a_tenant_that_never_subscribed_returns_404()
    {
        var response = await _clientA.GetAsync("/api/subscription/current");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCurrentUser_in_trial_has_no_plan()
    {
        var me = await (await _clientA.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<MeResponse>(JsonOptions);

        me!.PlanCode.Should().BeNull("a trial has not subscribed to any plan");
        me.HasSelectedPlan.Should().BeFalse();
    }

    [Fact]
    public async Task A_subscription_is_tenant_scoped()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var sub = UserSubscription.CreatePending(Guid.NewGuid(), TestConstants.TenantA, "a@wasnie.io", now);
            sub.UpdateFromStripe("pro", SubscriptionStatus.Active, "sub_scope_a", "cus_scope_a", "price_299", "prod_299",
                now, now.AddMonths(1), now.AddMonths(1), now);
            db.UserSubscriptions.Add(sub);
            await db.SaveChangesAsync();
        }

        var responseA = await _clientA.GetAsync("/api/subscription/current");
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        (await responseA.Content.ReadFromJsonAsync<SubscriptionResponse>(JsonOptions))!.PlanCode.Should().Be("pro");

        var responseB = await _clientB.GetAsync("/api/subscription/current");
        responseB.StatusCode.Should().Be(HttpStatusCode.NotFound, "TenantA's subscription is not visible to TenantB");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed record SubscriptionResponse(string? PlanCode, string Status, string? StripeSubscriptionId);

    private sealed record MeResponse(bool HasSelectedPlan, string? PlanCode);
}
