using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Plans;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PlansEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public PlansEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlans_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/plans");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Create Plan ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlan_ValidRequest_Returns201WithPlanDto()
    {
        var request = new
        {
            name = "Enterprise Sales Q1",
            description = "Test plan",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR"
        };

        var response = await _clientA.PostAsJsonAsync("/api/plans", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<PlanResponse>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("Enterprise Sales Q1");
        body.Status.Should().Be("Draft");
        body.Version.Should().Be(1);
        body.Currency.Should().Be("EUR");
        body.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreatePlan_EmptyName_Returns400()
    {
        var request = new
        {
            name = "",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR"
        };

        var response = await _clientA.PostAsJsonAsync("/api/plans", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePlan_EndBeforeStart_Returns400()
    {
        var request = new
        {
            name = "Bad Plan",
            description = "",
            effectiveStart = "2025-12-31",
            effectiveEnd = "2025-01-01",
            currency = "EUR"
        };

        var response = await _clientA.PostAsJsonAsync("/api/plans", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Get Plan ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlan_ExistingId_Returns200()
    {
        var created = await CreatePlanAsync(_clientA, "Get Test Plan");

        var response = await _clientA.GetAsync($"/api/plans/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanResponse>();
        body!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetPlan_UnknownId_Returns404()
    {
        var response = await _clientA.GetAsync($"/api/plans/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── List Plans ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPlans_ReturnsOnlyOwnTenantPlans()
    {
        await CreatePlanAsync(_clientA, "Tenant A Plan");
        await CreatePlanAsync(_clientB, "Tenant B Plan");

        var response = await _clientA.GetAsync("/api/plans");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<PlanSummaryResponse>>();
        body.Should().NotBeNull();
        body!.Should().ContainSingle(p => p.Name == "Tenant A Plan");
        body.Should().NotContain(p => p.Name == "Tenant B Plan");
    }

    [Fact]
    public async Task ListPlans_FilterByStatus_ReturnsMatchingPlans()
    {
        await CreatePlanAsync(_clientA, "Draft Plan");
        var activePlan = await CreatePlanAsync(_clientA, "To Activate");
        await AddRuleAsync(_clientA, activePlan.Id);
        await _clientA.PostAsync($"/api/plans/{activePlan.Id}/activate", null);

        var response = await _clientA.GetAsync("/api/plans?status=Active");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<PlanSummaryResponse>>();
        body.Should().NotBeNull();
        body!.Should().ContainSingle(p => p.Name == "To Activate");
        body.Should().NotContain(p => p.Name == "Draft Plan");
    }

    // ── Activate Plan ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivatePlan_DraftWithRule_Returns204()
    {
        var plan = await CreatePlanAsync(_clientA, "Ready Plan");
        await AddRuleAsync(_clientA, plan.Id);

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var check = await _clientA.GetAsync($"/api/plans/{plan.Id}");
        var body = await check.Content.ReadFromJsonAsync<PlanResponse>();
        body!.Status.Should().Be("Active");
    }

    [Fact]
    public async Task ActivatePlan_DraftWithNoRules_Returns422()
    {
        var plan = await CreatePlanAsync(_clientA, "Empty Plan");

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ActivatePlan_AlreadyActive_Returns422()
    {
        var plan = await CreatePlanAsync(_clientA, "Active Plan");
        await AddRuleAsync(_clientA, plan.Id);
        await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ActivatePlan_UnknownId_Returns422()
    {
        var response = await _clientA.PostAsync($"/api/plans/{Guid.NewGuid()}/activate", null);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Archive Plan ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchivePlan_ActivePlan_Returns204()
    {
        var plan = await CreatePlanAsync(_clientA, "To Archive");
        await AddRuleAsync(_clientA, plan.Id);
        await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/archive", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var check = await _clientA.GetAsync($"/api/plans/{plan.Id}");
        var body = await check.Content.ReadFromJsonAsync<PlanResponse>();
        body!.Status.Should().Be("Archived");
    }

    [Fact]
    public async Task ArchivePlan_AlreadyArchived_Returns422()
    {
        var plan = await CreatePlanAsync(_clientA, "Already Archived");
        await AddRuleAsync(_clientA, plan.Id);
        await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);
        await _clientA.PostAsync($"/api/plans/{plan.Id}/archive", null);

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/archive", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Clone Plan ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClonePlan_ActivePlan_Returns201WithVersionIncremented()
    {
        var plan = await CreatePlanAsync(_clientA, "Original");
        await AddRuleAsync(_clientA, plan.Id);
        await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/clone", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var clone = await response.Content.ReadFromJsonAsync<PlanResponse>();
        clone!.Name.Should().Be("Original");
        clone.Version.Should().Be(2);
        clone.Status.Should().Be("Draft");
        clone.Id.Should().NotBe(plan.Id);
    }

    // ── Delete Plan ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePlan_DraftWithNoRules_Returns204()
    {
        var plan = await CreatePlanAsync(_clientA, "To Delete");

        var response = await _clientA.DeleteAsync($"/api/plans/{plan.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var check = await _clientA.GetAsync($"/api/plans/{plan.Id}");
        check.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletePlan_DraftWithRules_Returns422()
    {
        var plan = await CreatePlanAsync(_clientA, "Plan With Rules");
        await AddRuleAsync(_clientA, plan.Id);

        var response = await _clientA.DeleteAsync($"/api/plans/{plan.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task DeletePlan_NonDraftPlan_Returns422()
    {
        var plan = await CreatePlanAsync(_clientA, "Active Plan To Delete");
        await AddRuleAsync(_clientA, plan.Id);
        await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);

        var response = await _clientA.DeleteAsync($"/api/plans/{plan.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Multi-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task GetPlan_CrossTenant_Returns404()
    {
        var planForA = await CreatePlanAsync(_clientA, "Tenant A Secret");

        var response = await _clientB.GetAsync($"/api/plans/{planForA.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ActivatePlan_CrossTenant_Returns422()
    {
        var planForA = await CreatePlanAsync(_clientA, "A's plan");
        await AddRuleAsync(_clientA, planForA.Id);

        var response = await _clientB.PostAsync($"/api/plans/{planForA.Id}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ArchivePlan_CrossTenant_Returns422()
    {
        var planForA = await CreatePlanAsync(_clientA, "A's Active Plan");
        await AddRuleAsync(_clientA, planForA.Id);
        await _clientA.PostAsync($"/api/plans/{planForA.Id}/activate", null);

        var response = await _clientB.PostAsync($"/api/plans/{planForA.Id}/archive", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ClonePlan_CrossTenant_Returns400()
    {
        var planForA = await CreatePlanAsync(_clientA, "A's Clonable Plan");
        await AddRuleAsync(_clientA, planForA.Id);
        await _clientA.PostAsync($"/api/plans/{planForA.Id}/activate", null);

        var response = await _clientB.PostAsync($"/api/plans/{planForA.Id}/clone", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeletePlan_CrossTenant_Returns422()
    {
        var planForA = await CreatePlanAsync(_clientA, "A's Draft Plan");

        var response = await _clientB.DeleteAsync($"/api/plans/{planForA.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AddRule_CrossTenant_Returns422()
    {
        var planForA = await CreatePlanAsync(_clientA, "A's Plan For Rule Test");

        var request = new
        {
            planId = planForA.Id,
            name = "Malicious Rule",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };

        var response = await _clientB.PostAsJsonAsync($"/api/plans/{planForA.Id}/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task QueryFilter_ConsecutiveRequestsDifferentTenants_SeeOnlyOwnData()
    {
        await CreatePlanAsync(_clientA, "A Only Plan");
        await CreatePlanAsync(_clientB, "B Only Plan");

        var aPlans = await (await _clientA.GetAsync("/api/plans"))
            .Content.ReadFromJsonAsync<List<PlanSummaryResponse>>();
        var bPlans = await (await _clientB.GetAsync("/api/plans"))
            .Content.ReadFromJsonAsync<List<PlanSummaryResponse>>();
        var aPlansAgain = await (await _clientA.GetAsync("/api/plans"))
            .Content.ReadFromJsonAsync<List<PlanSummaryResponse>>();

        aPlans!.Should().ContainSingle(p => p.Name == "A Only Plan");
        aPlans.Should().NotContain(p => p.Name == "B Only Plan");

        bPlans!.Should().ContainSingle(p => p.Name == "B Only Plan");
        bPlans.Should().NotContain(p => p.Name == "A Only Plan");

        aPlansAgain!.Should().ContainSingle(p => p.Name == "A Only Plan");
        aPlansAgain.Should().NotContain(p => p.Name == "B Only Plan");
    }

    // ── BUG-003 / BUG-004 integration verification ────────────────────────────

    [Fact]
    public async Task ArchivePlan_DraftPlan_Returns422()
    {
        var plan = await CreatePlanAsync(_clientA, "Draft To Archive");

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/archive", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ClonePlan_DraftSource_Returns400()
    {
        var plan = await CreatePlanAsync(_clientA, "Draft To Clone");

        var response = await _clientA.PostAsync($"/api/plans/{plan.Id}/clone", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<PlanResponse> CreatePlanAsync(HttpClient client, string name)
    {
        var request = new
        {
            name,
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR"
        };

        var response = await client.PostAsJsonAsync("/api/plans", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanResponse>())!;
    }

    private async Task AddRuleAsync(HttpClient client, Guid planId)
    {
        var request = new
        {
            planId,
            name = "Base Commission",
            sortOrder = 1,
            measurement = new
            {
                _schema = 1,
                type = 0,        // Revenue
                sourceField = "amount",
                aggregation = 0  // Sum
            },
            rateTable = new
            {
                _schema = 1,
                type = 0,        // Flat
                flatRate = 0.05
            },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };

        var response = await client.PostAsJsonAsync($"/api/plans/{planId}/rules", request);
        response.EnsureSuccessStatusCode();
    }

    private sealed record PlanResponse(Guid Id, string Name, string Status, int Version, string Currency, IEnumerable<object> Rules);
    private sealed record PlanSummaryResponse(Guid Id, string Name, string Status, int Version);
}
