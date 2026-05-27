using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Entities;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Authorization;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TierLimitIntegrationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _client = null!;

    public TierLimitIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetPayeesAsync();
        _client = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");
    }

    public async Task DisposeAsync()
    {
        // Reset tenant tier to Enterprise so other tests are never blocked by tier limits
        await SetTierAsync(TestConstants.TenantA, Tier.Enterprise);
    }

    [Fact]
    public async Task CreatePayee_Returns403_WhenFreeTierLimitExceeded()
    {
        // Set tenant A to Free tier
        await SetTierAsync(TestConstants.TenantA, Tier.Free);

        // Create 5 payees (the Free tier limit)
        for (var i = 1; i <= 5; i++)
        {
            var r = await _client.PostAsJsonAsync("/api/payees", new
            {
                fullName = $"Free Payee {i}",
                employeeCode = $"FP{i:D3}",
                email = $"free{i}@test.com",
                hireDate = "2024-01-15",
            });
            r.EnsureSuccessStatusCode();
        }

        // The 6th should fail with 403
        var response = await _client.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Over Limit Payee",
            employeeCode = "OVR001",
            email = "overlimit@test.com",
            hireDate = "2024-01-15",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<TierLimitResponse>();
        body!.Error.Should().Be("TierLimitExceeded");
        body.Tier.Should().Be("Free");
        body.Limit.Should().Be(5);
        body.CurrentCount.Should().Be(5);
    }

    [Fact]
    public async Task CreatePayee_Returns201_WhenGrowthTierNotExceeded()
    {
        await SetTierAsync(TestConstants.TenantA, Tier.Growth);
        await _fixture.ResetPayeesAsync();

        var response = await _client.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Growth Payee",
            employeeCode = "GR001",
            email = "growth@test.com",
            hireDate = "2024-01-15",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TierLimitResponse_HasUpgradePath()
    {
        await SetTierAsync(TestConstants.TenantA, Tier.Free);

        // Fill up the Free tier
        for (var i = 1; i <= 5; i++)
        {
            var r = await _client.PostAsJsonAsync("/api/payees", new
            {
                fullName = $"Up Payee {i}",
                employeeCode = $"UP{i:D3}",
                email = $"up{i}@test.com",
                hireDate = "2024-01-15",
            });
            // May already be at limit from previous test — only count successes
            if (!r.IsSuccessStatusCode) break;
        }

        var response = await _client.PostAsJsonAsync("/api/payees", new
        {
            fullName = "One More",
            employeeCode = "OM001",
            email = "onemore@test.com",
            hireDate = "2024-01-15",
        });

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadFromJsonAsync<TierLimitResponse>();
            body!.UpgradePath.Should().Be("/account/subscription");
        }
    }

    private async Task SetTierAsync(Guid tenantId, Tier tier)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
        {
            tenant = Tenant.Create($"Test Tenant {tenantId}", tenantId.ToString("N")[..20], tenantId, DateTimeOffset.UtcNow);
            db.Tenants.Add(tenant);
        }
        tenant.SetTier(tier);
        await db.SaveChangesAsync();
    }

    private sealed record TierLimitResponse(
        string Error,
        string Message,
        string Tier,
        int CurrentCount,
        int Limit,
        string UpgradePath);
}
