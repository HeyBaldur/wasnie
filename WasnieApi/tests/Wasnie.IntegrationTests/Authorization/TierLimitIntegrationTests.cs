using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Features.Subscription;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Authorization;

/// <summary>
/// Plan limits end to end (KAN-77: limits come from the plan catalog, not a tier table).
///
/// ★ Two catalogs on purpose. The SHIPPED one (one unlimited plan) proves the old Free cap of 5 payees is gone.
/// A capped THREE-plan catalog, injected per test, proves the limit machinery still refuses with the same 403
/// shape the client's limit modal reads — the day a capped plan is sold.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TierLimitIntegrationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;

    public TierLimitIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetPayeesAsync();

    public async Task DisposeAsync()
    {
        await SetPlanCodeAsync(null);
        await _fixture.ResetPayeesAsync();
    }

    private async Task SetPlanCodeAsync(string? planCode)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE Tenants SET PlanCode = {planCode} WHERE Id = {TestConstants.TenantA}");
    }

    private WebApplicationFactory<Program> CappedCatalogFactory() =>
        _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<ISubscriptionPlanCatalog>(TestPlans.ThreePlanCatalog())));

    private static Task<HttpResponseMessage> CreatePayeeAsync(HttpClient client, int i) =>
        client.PostAsJsonAsync("/api/payees", new
        {
            fullName = $"Limit Payee {i}",
            employeeCode = $"LIM{i:D3}",
            email = $"limit{i}@test.com",
            hireDate = "2024-01-15",
        });

    [Fact]
    public async Task With_the_shipped_plan_the_old_Free_cap_of_five_payees_is_gone()
    {
        var client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");

        for (var i = 1; i <= 6; i++)
            (await CreatePayeeAsync(client, i)).StatusCode.Should().Be(HttpStatusCode.Created, $"payee {i}");
    }

    [Fact]
    public async Task A_capped_plan_refuses_the_payee_past_its_limit_with_the_limit_payload()
    {
        await SetPlanCodeAsync("starter"); // 25 payees in the capped catalog
        using var factory = CappedCatalogFactory();
        var client = factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");

        for (var i = 1; i <= 25; i++)
            (await CreatePayeeAsync(client, i)).EnsureSuccessStatusCode();

        var response = await CreatePayeeAsync(client, 26);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<TierLimitResponse>();
        body!.Error.Should().Be("TierLimitExceeded");
        body.Tier.Should().Be("starter");
        body.Limit.Should().Be(25);
        body.CurrentCount.Should().Be(25);
        body.UpgradePath.Should().Be("/account/subscription");
    }

    [Fact]
    public async Task A_trial_experiences_the_default_plan()
    {
        // No PlanCode (the shared test tenants are in trial): the capped catalog's default is "scale", 150 payees.
        using var factory = CappedCatalogFactory();
        var client = factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");

        for (var i = 1; i <= 26; i++)
            (await CreatePayeeAsync(client, i)).StatusCode.Should().Be(HttpStatusCode.Created,
                "past the starter cap: a trial is held to the default plan, not the smallest one");
    }

    private sealed record TierLimitResponse(
        string Error,
        string Message,
        string Tier,
        int CurrentCount,
        int Limit,
        string UpgradePath);
}
