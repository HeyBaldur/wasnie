using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Helpers;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Plans;

/// <summary>
/// POST /api/plans/with-quota over real HTTP against a real database.
///
/// The handler tests pin the ordering (SaveChanges never called on a refused request). What only this
/// level can show is the STATUS CODE and the state of the two tables afterwards — specifically that a
/// rejected quota leaves no plan behind, which is the failure the whole command exists to prevent.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class CreatePlanWithQuotaEndpointTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;

    public CreatePlanWithQuotaEndpointTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetCompensationDataAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/plans/with-quota", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidRequest_Creates201WithPlanAndQuotasLinked()
    {
        var payee = await CreatePayeeAsync(_clientA, "PWQ001");

        var response = await _clientA.PostAsJsonAsync("/api/plans/with-quota", new
        {
            name = "Accelerator With Target",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
            quotas = new[] { QuotaSpec(payee.Id, "2025-01-01", "2025-03-31") }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = (await response.Content.ReadFromJsonAsync<ResultBody>())!;
        body.Failures.Should().BeEmpty();
        body.Plan!.Name.Should().Be("Accelerator With Target");
        body.Quotas.Should().ContainSingle();

        // The quota really hangs off the plan that was just created, in the database.
        body.Quotas[0].PlanId.Should().Be(body.Plan.Id);
        (await QuotaCountForPlanAsync(body.Plan.Id)).Should().Be(1);
    }

    [Fact]
    public async Task InvalidQuota_Returns400_AndTheRESULTINGPlanDoesNotExist()
    {
        // ★ THE ATOMICITY TEST AT HTTP LEVEL. The quota period falls outside the plan's effective
        // period — the same rule POST /api/quotas enforces. What matters is not only the 400: it is
        // that the plan named here exists NOWHERE afterwards. A handler that saved the plan first
        // would return this same 400 and leave a plan that pays €0 forever.
        var payee = await CreatePayeeAsync(_clientA, "PWQ002");
        var plansBefore = await PlanCountAsync();

        var response = await _clientA.PostAsJsonAsync("/api/plans/with-quota", new
        {
            name = "Plan That Must Not Survive",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
            quotas = new[]
            {
                QuotaSpec(payee.Id, "2025-01-01", "2025-03-31"),   // valid
                QuotaSpec(payee.Id, "2024-01-01", "2024-03-31"),   // outside the plan period
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = (await response.Content.ReadFromJsonAsync<ResultBody>())!;
        body.Plan.Should().BeNull();
        body.Quotas.Should().BeEmpty();
        body.Failures.Should().ContainSingle();
        body.Failures[0].Index.Should().Be(1);
        body.Failures[0].PayeeId.Should().Be(payee.Id);

        (await PlanNamedExistsAsync("Plan That Must Not Survive"))
            .Should().BeFalse("★ the plan must not outlive its rejected quota");
        (await PlanCountAsync()).Should().Be(plansBefore, "nothing at all was written");
    }

    [Fact]
    public async Task ItRejectsWhatTheSingleQuotaCreateRejects()
    {
        // Same offending input, both paths, same refusal — they share QuotaBuilder.
        var payee = await CreatePayeeAsync(_clientA, "PWQ003");

        var composite = await _clientA.PostAsJsonAsync("/api/plans/with-quota", new
        {
            name = "Parity Plan",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
            quotas = new[] { QuotaSpec(payee.Id, "2024-01-01", "2024-03-31") }
        });

        var plan = await CreatePlanAsync(_clientA, "Parity Reference Plan");
        var single = await _clientA.PostAsJsonAsync("/api/quotas", new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2024-01-01",
            periodEnd = "2024-03-31"
        });

        composite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        single.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmptyQuotaList_Returns400()
    {
        // A plan with no quotas is what POST /api/plans is for. This endpoint refusing the empty list
        // is what stops it from becoming a second way to do that.
        var response = await _clientA.PostAsJsonAsync("/api/plans/with-quota", new
        {
            name = "No Quotas",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
            quotas = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PlanNamedExistsAsync("No Quotas")).Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object QuotaSpec(Guid payeeId, string start, string end) => new
    {
        payeeId,
        measurementType = 0,
        amount = 10000m,
        currency = "EUR",
        periodStart = start,
        periodEnd = end,
    };

    private async Task<PayeeResponse> CreatePayeeAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/payees", new
        {
            fullName = $"Test Payee {code}",
            employeeCode = code,
            email = $"{code.ToLower()}@test.com",
            hireDate = "2024-01-01"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayeeResponse>())!;
    }

    private async Task<PlanResponse> CreatePlanAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/plans", new
        {
            name,
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanResponse>())!;
    }

    private async Task<int> PlanCountAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.CompensationPlans.IgnoreQueryFilters().CountAsync();
    }

    private async Task<bool> PlanNamedExistsAsync(string name)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.CompensationPlans.IgnoreQueryFilters().AnyAsync(p => p.Name == name);
    }

    private async Task<int> QuotaCountForPlanAsync(Guid planId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Quotas.IgnoreQueryFilters().CountAsync(q => q.PlanId == planId);
    }

    private sealed record PayeeResponse(Guid Id, string FullName, string EmployeeCode);
    private sealed record PlanResponse(Guid Id, string Name, string Status, int Version);
    private sealed record QuotaBody(Guid Id, Guid PayeeId, Guid PlanId, decimal Amount, string Currency);
    private sealed record FailureBody(int Index, Guid PayeeId, string PayeeName, string Reason);
    private sealed record ResultBody(
        PlanResponse? Plan, IReadOnlyList<QuotaBody> Quotas, IReadOnlyList<FailureBody> Failures);
}
