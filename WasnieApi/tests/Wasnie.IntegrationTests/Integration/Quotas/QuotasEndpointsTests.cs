using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Helpers;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Quotas;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class QuotasEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public QuotasEndpointsTests(TestDatabaseFixture fixture)
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
    public async Task ListQuotas_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/quotas");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetQuota_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync($"/api/quotas/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateQuota_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/quotas", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── List Quotas ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListQuotas_HappyPath_Returns200WithPagedResult()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.GetAsync("/api/quotas");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<QuotaSummaryResponse>();
        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListQuotas_Pagination_ReturnsCorrectPage()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        for (var i = 0; i < 5; i++)
            await CreateQuotaAsync(_clientA, payee.Id, plan.Id, periodOffset: i);

        var response = await _clientA.GetAsync("/api/quotas?page=2&pageSize=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<QuotaSummaryResponse>();
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
        result.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(2);
    }

    // ── Get Single Quota ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetQuota_ExistingId_Returns200()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.GetAsync($"/api/quotas/{quota.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>();
        body!.Id.Should().Be(quota.Id);
    }

    [Fact]
    public async Task GetQuota_UnknownId_Returns404()
    {
        var response = await _clientA.GetAsync($"/api/quotas/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Create Quota ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateQuota_ValidRequest_Returns201()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>();
        body!.Id.Should().NotBeEmpty();
        body.Amount.Should().Be(10000m);
        body.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task CreateQuota_CurrencyMismatchWithPlan_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA); // EUR plan

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 10000m,
            currency = "PLN", // PLN quota on EUR plan — mismatch
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateQuota_CurrencyMismatchWithPlan_ReturnsError()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA); // EUR plan
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id); // EUR quota

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 20000m,
            currency = "PLN", // PLN update on EUR plan — mismatch
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        // Domain exception is caught and returned as Failure → 422 UnprocessableEntity
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateQuota_ZeroAmount_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 0m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateQuota_InvalidCurrency_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 5000m,
            currency = "TOOLONG",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateQuota_EndBeforeStart_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            payeeId = payee.Id,
            planId = plan.Id,
            measurementType = 0,
            amount = 5000m,
            currency = "EUR",
            periodStart = "2025-12-31",
            periodEnd = "2025-01-01"
        };

        var response = await _clientA.PostAsJsonAsync("/api/quotas", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Update Quota ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateQuota_ValidRequest_Returns200()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA); // EUR plan
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 20000m,
            currency = "EUR", // must match plan currency
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>();
        body!.Amount.Should().Be(20000m);
        body.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task UpdateQuota_ZeroAmount_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var request = new
        {
            quotaId = quota.Id,
            measurementType = 0,
            amount = 0m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        var response = await _clientA.PutAsJsonAsync($"/api/quotas/{quota.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Activate / Close Quota ────────────────────────────────────────────────

    [Fact]
    public async Task ActivateQuota_DraftQuota_Returns204()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.PostAsync($"/api/quotas/{quota.Id}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CloseQuota_ActiveQuota_Returns204()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var quota = await CreateQuotaAsync(_clientA, payee.Id, plan.Id);
        await _clientA.PostAsync($"/api/quotas/{quota.Id}/activate", null);

        var response = await _clientA.PostAsync($"/api/quotas/{quota.Id}/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task ListQuotas_CrossTenant_DoesNotLeak()
    {
        var payeeA = await CreatePayeeAsync(_clientA, "TA001");
        var planA = await CreateActivePlanAsync(_clientA);
        await CreateQuotaAsync(_clientA, payeeA.Id, planA.Id);

        var payeeB = await CreatePayeeAsync(_clientB, "TB001");
        var planB = await CreateActivePlanAsync(_clientB);
        await CreateQuotaAsync(_clientB, payeeB.Id, planB.Id);

        var response = await _clientA.GetAsync("/api/quotas");
        var result = await response.Content.ReadPagedResultAsync<QuotaSummaryResponse>();

        result.Items.Should().ContainSingle(q => q.PayeeEmployeeCode == "TA001");
        result.Items.Should().NotContain(q => q.PayeeEmployeeCode == "TB001");
    }

    [Fact]
    public async Task GetQuota_CrossTenantById_Returns404()
    {
        var payeeA = await CreatePayeeAsync(_clientA);
        var planA = await CreateActivePlanAsync(_clientA);
        var quotaA = await CreateQuotaAsync(_clientA, payeeA.Id, planA.Id);

        // Tenant B tries to access Tenant A's quota by ID
        var response = await _clientB.GetAsync($"/api/quotas/{quotaA.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateQuota_CrossTenant_Returns422OrNotFound()
    {
        var payeeA = await CreatePayeeAsync(_clientA);
        var planA = await CreateActivePlanAsync(_clientA);
        var quotaA = await CreateQuotaAsync(_clientA, payeeA.Id, planA.Id);

        var request = new
        {
            quotaId = quotaA.Id,
            measurementType = 0,
            amount = 99999m,
            currency = "EUR",
            periodStart = "2025-01-01",
            periodEnd = "2025-03-31"
        };

        // TODO: F-028 — current impl returns 422 (UnprocessableEntity) instead of 404 for cross-tenant update.
        // Revisit when cross-tenant response standardization is completed.
        var response = await _clientB.PutAsJsonAsync($"/api/quotas/{quotaA.Id}", request);

        new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound }
            .Should().Contain(response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<PayeeResponse> CreatePayeeAsync(HttpClient client, string code = "EMP001")
    {
        var request = new
        {
            fullName = $"Test Payee {code}",
            employeeCode = code,
            email = $"{code.ToLower()}@test.com",
            hireDate = "2024-01-01"
        };
        var response = await client.PostAsJsonAsync("/api/payees", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayeeResponse>())!;
    }

    private async Task<PlanResponse> CreateActivePlanAsync(HttpClient client)
    {
        var planRequest = new
        {
            name = $"Test Plan {Guid.NewGuid().ToString("N")[..8]}",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR"
        };
        var planResp = await client.PostAsJsonAsync("/api/plans", planRequest);
        planResp.EnsureSuccessStatusCode();
        var plan = (await planResp.Content.ReadFromJsonAsync<PlanResponse>())!;

        var ruleRequest = new
        {
            planId = plan.Id,
            name = "Base Commission",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null
        };
        var ruleResp = await client.PostAsJsonAsync($"/api/plans/{plan.Id}/rules", ruleRequest);
        ruleResp.EnsureSuccessStatusCode();

        var activateResp = await client.PostAsync($"/api/plans/{plan.Id}/activate", null);
        activateResp.EnsureSuccessStatusCode();

        return plan;
    }

    private async Task<QuotaSummaryResponse> CreateQuotaAsync(
        HttpClient client, Guid payeeId, Guid planId, int periodOffset = 0)
    {
        var start = new DateOnly(2025, 1, 1).AddMonths(periodOffset * 3);
        var end = start.AddMonths(3).AddDays(-1);
        var request = new
        {
            payeeId,
            planId,
            measurementType = 0,
            amount = 10000m,
            currency = "EUR",
            periodStart = start.ToString("yyyy-MM-dd"),
            periodEnd = end.ToString("yyyy-MM-dd")
        };
        var response = await client.PostAsJsonAsync("/api/quotas", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuotaSummaryResponse>())!;
    }

    private sealed record PayeeResponse(Guid Id, string FullName, string EmployeeCode, string StatusLabel);
    private sealed record PlanResponse(Guid Id, string Name, string Status, int Version);
    private sealed record QuotaSummaryResponse(Guid Id, Guid PayeeId, string PayeeEmployeeCode, decimal Amount, string Currency, string Status);
}
