using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Plans;

/// <summary>
/// The clawback policy has to survive the ROUND TRIP, not just the write.
///
/// The bug these pin shut: the PUT stored 180/50 correctly and the GET answered null/null, because
/// the DTO gave both fields a default of null and the mapper stopped one line short. Every layer
/// "worked" — the screen showed the policy empty and said "Not active" over a plan that had one
/// stored, and the green "saved" toast was telling the truth about a write nobody could see.
///
/// So these assert the JSON ON THE WIRE, not a typed record: a deserialiser with the same defaults
/// as the DTO would reproduce the bug and still pass.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PlanClawbackPolicyEndpointsTests(TestDatabaseFixture fixture)
{
    private HttpClient Client() => fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);

    private static async Task<Guid> CreatePlanAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/plans", new
        {
            name = $"Clawback Plan {Guid.NewGuid().ToString("N")[..8]}",
            description = "",
            effectiveStart = "2026-01-01",
            effectiveEnd = "2026-12-31",
            currency = "EUR",
        });
        response.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetPlanAsync(HttpClient client, Guid planId)
    {
        var response = await client.GetAsync($"/api/plans/{planId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task A_saved_policy_comes_back_on_the_next_read()
    {
        var client = Client();
        var planId = await CreatePlanAsync(client);

        var save = await client.PutAsJsonAsync(
            $"/api/plans/{planId}/clawback-policy", new { maturationDays = 180, capPercent = 50m });
        save.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var plan = await GetPlanAsync(client, planId);

        plan.GetProperty("clawbackMaturationDays").GetInt32().Should().Be(180);
        plan.GetProperty("clawbackCapPercent").GetDecimal().Should().Be(50m);
    }

    [Fact]
    public async Task What_the_read_returns_is_what_the_database_holds()
    {
        // The assertion the old code would have failed even while the row was perfect: the wire value
        // is compared against the STORED value, so a mapper that answers a plausible null is caught.
        var client = Client();
        var planId = await CreatePlanAsync(client);

        await client.PutAsJsonAsync(
            $"/api/plans/{planId}/clawback-policy", new { maturationDays = 90, capPercent = 25m });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.CompensationPlans.IgnoreQueryFilters()
            .Where(p => p.Id == planId)
            .Select(p => new { p.ClawbackMaturationDays, p.ClawbackCapPercent })
            .SingleAsync();

        stored.ClawbackMaturationDays.Should().Be(90, "precondition: the write path stores the policy");

        var plan = await GetPlanAsync(client, planId);

        plan.GetProperty("clawbackMaturationDays").GetInt32().Should().Be(stored.ClawbackMaturationDays!.Value);
        plan.GetProperty("clawbackCapPercent").GetDecimal().Should().Be(stored.ClawbackCapPercent!.Value);
    }

    [Fact]
    public async Task A_plan_without_a_policy_still_reads_as_null()
    {
        // The control. Null is the correct answer for the vast majority of plans — the fix must not
        // turn "no policy" into some invented number, which is the opposite failure.
        var client = Client();
        var planId = await CreatePlanAsync(client);

        var plan = await GetPlanAsync(client, planId);

        plan.GetProperty("clawbackMaturationDays").ValueKind.Should().Be(JsonValueKind.Null);
        plan.GetProperty("clawbackCapPercent").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Turning_the_policy_off_reads_back_as_off()
    {
        var client = Client();
        var planId = await CreatePlanAsync(client);

        await client.PutAsJsonAsync(
            $"/api/plans/{planId}/clawback-policy", new { maturationDays = 180, capPercent = 50m });
        await client.PutAsJsonAsync(
            $"/api/plans/{planId}/clawback-policy",
            new { maturationDays = (int?)null, capPercent = (decimal?)null });

        var plan = await GetPlanAsync(client, planId);

        plan.GetProperty("clawbackMaturationDays").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
