using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Payees;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeeDashboardEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;

    public PayeeDashboardEndpointsTests(TestDatabaseFixture fixture)
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
    public async Task GetDashboard_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync($"/api/payees/{Guid.NewGuid()}/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDashboard_NewPayeeNoData_Returns200WithEmptyCards()
    {
        var payeeId = await CreatePayeeAsync("EMP-DASH-001");

        var response = await _clientA.GetAsync($"/api/payees/{payeeId}/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        body.Should().NotBeNull();
        body!.AttainmentItems.Should().BeEmpty();
        body.EarningsTrend.Should().BeEmpty();
        body.RecentQuotas.Should().BeEmpty();
        body.RecentAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboard_WithQuotaAndAssignment_Returns200WithAttainmentData()
    {
        var payeeId = await CreatePayeeAsync("EMP-DASH-002");
        var planId = await CreateActivePlanAsync();
        await CreateAssignmentAsync(payeeId, planId);
        await CreateAndActivateQuotaAsync(payeeId, planId);

        var response = await _clientA.GetAsync($"/api/payees/{payeeId}/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        body.Should().NotBeNull();
        // Attainment items: quota is for 2026 (active period) → 1 item
        body!.AttainmentItems.Should().HaveCount(1);
        body.AttainmentItems[0].AttainmentValue.Should().Be(0m); // no credits → 0%
        // RecentQuotas and RecentAssignments are now empty — lists served by separate paginated endpoints
        body.RecentQuotas.Should().BeEmpty();
        body.RecentAssignments.Should().BeEmpty();
        body.EarningsTrend.Should().BeEmpty(); // no credits seeded
    }

    [Fact]
    public async Task GetDashboard_WithPeriodAll_Returns200()
    {
        var payeeId = await CreatePayeeAsync("EMP-DASH-003");

        var response = await _clientA.GetAsync($"/api/payees/{payeeId}/dashboard?period=all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>();
        body.Should().NotBeNull();
        body!.AttainmentItems.Should().BeEmpty(); // no quotas
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Guid> CreatePayeeAsync(string code)
    {
        var request = new
        {
            fullName = $"Dash Payee {code}",
            employeeCode = code,
            email = $"{code.ToLower()}@test.com",
            hireDate = "2024-01-01"
        };
        var response = await _clientA.PostAsJsonAsync("/api/payees", request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateActivePlanAsync()
    {
        var planReq = new
        {
            name = $"Dash Plan {Guid.NewGuid().ToString("N")[..6]}",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2026-12-31",
            currency = "EUR"
        };
        var planResp = await _clientA.PostAsJsonAsync("/api/plans", planReq);
        planResp.EnsureSuccessStatusCode();
        var planBody = await planResp.Content.ReadFromJsonAsync<JsonElement>();
        var planId = planBody.GetProperty("id").GetGuid();

        var ruleReq = new
        {
            planId,
            name = "Rule",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };
        await _clientA.PostAsJsonAsync($"/api/plans/{planId}/rules", ruleReq);
        (await _clientA.PostAsync($"/api/plans/{planId}/activate", null)).EnsureSuccessStatusCode();
        return planId;
    }

    private async Task CreateAssignmentAsync(Guid payeeId, Guid planId)
    {
        var req = new { planId, payeeId, effectiveStart = "2025-01-01", effectiveEnd = "2026-12-31" };
        (await _clientA.PostAsJsonAsync("/api/assignments", req)).EnsureSuccessStatusCode();
    }

    private async Task CreateAndActivateQuotaAsync(Guid payeeId, Guid planId)
    {
        var req = new
        {
            payeeId,
            planId,
            measurementType = 0,
            amount = 50000m,
            currency = "EUR",
            periodStart = "2026-01-01",
            periodEnd = "2026-12-31"
        };
        var resp = await _clientA.PostAsJsonAsync("/api/quotas", req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var quotaId = body.GetProperty("id").GetGuid();
        (await _clientA.PostAsync($"/api/quotas/{quotaId}/activate", null)).EnsureSuccessStatusCode();
    }

    private sealed record DashboardResponse(
        AttainmentItemResponse[] AttainmentItems,
        object[] EarningsTrend,
        object[] RecentQuotas,
        object[] RecentAssignments);

    private sealed record AttainmentItemResponse(
        Guid QuotaId,
        decimal AttainmentValue,
        string AttainmentPercent);
}
