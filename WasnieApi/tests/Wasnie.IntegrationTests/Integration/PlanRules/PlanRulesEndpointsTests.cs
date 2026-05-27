using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.PlanRules;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PlanRulesEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public PlanRulesEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetCompensationDataAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRule_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/plans/{Guid.NewGuid()}/rules", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteRule_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.DeleteAsync($"/api/plans/{Guid.NewGuid()}/rules/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Add Rule ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRule_ValidFlatRate_Returns200WithRuleDto()
    {
        var plan = await CreatePlanAsync(_clientA);

        var request = BuildFlatRuleRequest(plan.Id);
        var response = await _clientA.PostAsJsonAsync($"/api/plans/{plan.Id}/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RuleResponse>();
        body!.Id.Should().NotBeEmpty();
        body.Name.Should().Be("Base Commission");
    }

    [Fact]
    public async Task AddRule_EmptyName_Returns400()
    {
        var plan = await CreatePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            name = "",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };

        var response = await _clientA.PostAsJsonAsync($"/api/plans/{plan.Id}/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddRule_MissingMeasurement_Returns400()
    {
        var plan = await CreatePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            name = "Bad Rule",
            sortOrder = 1,
            measurement = (object?)null,
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };

        var response = await _clientA.PostAsJsonAsync($"/api/plans/{plan.Id}/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddRule_RouteIdMismatchBodyId_Returns400()
    {
        var plan = await CreatePlanAsync(_clientA);
        var differentId = Guid.NewGuid();

        var request = BuildFlatRuleRequest(differentId);
        var response = await _clientA.PostAsJsonAsync($"/api/plans/{plan.Id}/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Update Rule ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRule_ValidRequest_Returns200()
    {
        var plan = await CreatePlanAsync(_clientA);
        var rule = await AddRuleAsync(_clientA, plan.Id);

        var request = new
        {
            planId = plan.Id,
            ruleId = rule.Id,
            name = "Updated Commission",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.08 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };

        var response = await _clientA.PutAsJsonAsync($"/api/plans/{plan.Id}/rules/{rule.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RuleResponse>();
        body!.Name.Should().Be("Updated Commission");
    }

    // ── Delete Rule ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRule_ExistingRule_Returns204()
    {
        var plan = await CreatePlanAsync(_clientA);
        var rule = await AddRuleAsync(_clientA, plan.Id);

        var response = await _clientA.DeleteAsync($"/api/plans/{plan.Id}/rules/{rule.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteRule_UnknownRuleId_Returns422()
    {
        var plan = await CreatePlanAsync(_clientA);

        var response = await _clientA.DeleteAsync($"/api/plans/{plan.Id}/rules/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task AddRule_CrossTenant_Returns422OrNotFound()
    {
        var planA = await CreatePlanAsync(_clientA);

        var request = BuildFlatRuleRequest(planA.Id);

        // TODO: F-028 — current impl returns 422 (UnprocessableEntity) instead of 404 for cross-tenant mutations.
        var response = await _clientB.PostAsJsonAsync($"/api/plans/{planA.Id}/rules", request);

        new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound }
            .Should().Contain(response.StatusCode);
    }

    [Fact]
    public async Task DeleteRule_CrossTenant_Returns422OrNotFound()
    {
        var planA = await CreatePlanAsync(_clientA);
        var ruleA = await AddRuleAsync(_clientA, planA.Id);

        // TODO: F-028 — current impl returns 422 (UnprocessableEntity) instead of 404 for cross-tenant mutations.
        var response = await _clientB.DeleteAsync($"/api/plans/{planA.Id}/rules/{ruleA.Id}");

        new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound }
            .Should().Contain(response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<PlanResponse> CreatePlanAsync(HttpClient client)
    {
        var request = new
        {
            name = $"Test Plan {Guid.NewGuid().ToString("N")[..8]}",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR"
        };
        var response = await client.PostAsJsonAsync("/api/plans", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanResponse>())!;
    }

    private async Task<RuleResponse> AddRuleAsync(HttpClient client, Guid planId)
    {
        var request = BuildFlatRuleRequest(planId);
        var response = await client.PostAsJsonAsync($"/api/plans/{planId}/rules", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RuleResponse>())!;
    }

    private static object BuildFlatRuleRequest(Guid planId) => new
    {
        planId,
        name = "Base Commission",
        sortOrder = 1,
        measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
        rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
        trigger = (object?)null,
        modifier = (object?)null,
        cap = (object?)null,
        floor = (object?)null
    };

    private sealed record PlanResponse(Guid Id, string Name, string Status, int Version);
    private sealed record RuleResponse(Guid Id, string Name, int SortOrder);
}
