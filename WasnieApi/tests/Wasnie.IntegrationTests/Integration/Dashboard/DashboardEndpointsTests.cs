using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Dashboard;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class DashboardEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public DashboardEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetDashboardDataAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Auth ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Empty tenant — all zeros / nulls ────────────────────────────────────

    [Fact]
    public async Task GetDashboard_EmptyTenant_Returns200WithZeroKpis()
    {
        var response = await _clientA.GetAsync("/api/dashboard?period=this-month");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        body.Should().NotBeNull();

        body!.ActionBand.DraftPayRunsCount.Should().Be(0);
        body.ActionBand.PayoutsPendingApprovalCount.Should().Be(0);
        body.ActionBand.PayoutsPendingApprovalByCurrency.Should().BeEmpty();
        body.ActionBand.PayoutsApprovedUnpaidByCurrency.Should().BeEmpty();
        body.ActionBand.PendingByPlanItems.Should().BeEmpty();

        body.PeriodBand.TransactionsCount.Should().Be(0);
        body.PeriodBand.TransactionsVolumeByCurrency.Should().BeEmpty();
        body.PeriodBand.PayoutsTotalByCurrency.Should().BeEmpty();
        body.PeriodBand.CreditsCount.Should().Be(0);
        body.PeriodBand.CreditsTotalByCurrency.Should().BeEmpty();
        body.PeriodBand.AvgQuotaAttainmentPercent.Should().BeNull();
        body.PeriodBand.ActivePlansCount.Should().Be(0);
        body.PeriodBand.ActiveQuotasCount.Should().Be(0);
        body.PeriodBand.PayeesActiveCount.Should().Be(0);
        body.PeriodBand.PayeesInactiveCount.Should().Be(0);
    }

    // ── Multi-tenant isolation ──────────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_ActivePlansCount_IsTenantScoped()
    {
        // Create a plan in Tenant A
        await CreateActivePlanAsync(_clientA, "EUR");

        var responseA = await _clientA.GetAsync("/api/dashboard?period=ytd");
        var responseB = await _clientB.GetAsync("/api/dashboard?period=ytd");

        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);

        var bodyA = await responseA.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        var bodyB = await responseB.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        bodyA!.PeriodBand.ActivePlansCount.Should().Be(1, "Tenant A created 1 plan");
        bodyB!.PeriodBand.ActivePlansCount.Should().Be(0, "Tenant B created no plans");
    }

    [Fact]
    public async Task GetDashboard_PayeesCount_IsTenantScoped()
    {
        await CreatePayeeAsync(_clientA, "DASH-MT-001");
        await CreatePayeeAsync(_clientA, "DASH-MT-002");

        var responseA = await _clientA.GetAsync("/api/dashboard?period=ytd");
        var responseB = await _clientB.GetAsync("/api/dashboard?period=ytd");

        var bodyA = await responseA.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        var bodyB = await responseB.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        bodyA!.PeriodBand.PayeesActiveCount.Should().Be(2, "Tenant A has 2 payees");
        bodyB!.PeriodBand.PayeesActiveCount.Should().Be(0, "Tenant B has no payees");
    }

    // ── Anti-Cartesian attainment test (mandatory per WI spec) ──────────────
    // Invariant: 2 quotas seeded at 50% and 100% attainment → avg = 75%.
    // Verifies that joining Credits × Transactions × Quotas does NOT multiply rows,
    // which would distort the average away from 75%.

    [Fact]
    public async Task GetDashboard_AvgQuotaAttainment_TwoQuotasAt50And100Percent_Returns75()
    {
        // Seed: 2 payees, 1 plan, 2 quotas (target 10,000 each)
        var payeeId1 = await CreatePayeeAsync(_clientA, "DASH-ATT-001");
        var payeeId2 = await CreatePayeeAsync(_clientA, "DASH-ATT-002");
        var planId = await CreateActivePlanAsync(_clientA, "EUR");

        await CreateAssignmentAsync(_clientA, payeeId1, planId);
        await CreateAssignmentAsync(_clientA, payeeId2, planId);

        // Quotas: target = 10,000 EUR, period = 2026
        await CreateAndActivateQuotaAsync(_clientA, payeeId1, planId, target: 10_000m);
        await CreateAndActivateQuotaAsync(_clientA, payeeId2, planId, target: 10_000m);

        // Transactions: payee 1 = 5,000 (50%), payee 2 = 10,000 (100%)
        await CreateTransactionAsync(_clientA, payeeId1, planId, amount: 5_000m, currency: "EUR");
        await CreateTransactionAsync(_clientA, payeeId2, planId, amount: 10_000m, currency: "EUR");

        // Process pending to allocate credits (async Hangfire job — poll up to 5 s)
        var processReq = new { scope = 0, scopeId = planId };
        (await _clientA.PostAsJsonAsync("/api/transactions/process-pending", processReq))
            .EnsureSuccessStatusCode();

        DashboardResponse? body = null;
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var resp = await _clientA.GetAsync("/api/dashboard?period=ytd");
            body = await resp.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
            if (body?.PeriodBand.AvgQuotaAttainmentPercent.HasValue == true) break;
        }

        body.Should().NotBeNull();
        body!.PeriodBand.AvgQuotaAttainmentPercent.Should().NotBeNull(
            "two quotas with credits should produce an attainment value");

        // Anti-Cartesian invariant: result must be exactly 75% (50%+100%)/2, not distorted.
        body.PeriodBand.AvgQuotaAttainmentPercent!.Value
            .Should().BeApproximately(75m, 1m,
                because: "avg of 50% and 100% attainment must equal 75%, not a Cartesian-inflated value");
    }

    // ── Trend band present for period-comparable periods ────────────────────

    [Fact]
    public async Task GetDashboard_TrendBand_IsPresentForThisMonth()
    {
        var response = await _clientA.GetAsync("/api/dashboard?period=this-month");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        body!.TrendBand.Should().NotBeNull("this-month has a prior period (last-month)");
        body.TrendBand!.PriorPeriodLabel.Should().NotBeNullOrEmpty();
        body.TrendBand.CurrentPeriodLabel.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDashboard_TrendBand_IsNullForAllTime()
    {
        var response = await _clientA.GetAsync("/api/dashboard?period=all-time");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        body!.TrendBand.Should().BeNull("all-time has no comparable prior period");
    }

    // ── PendingByPlanItems ──────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_PendingByPlanItems_IsEmptyWhenNoAssignmentsOrTransactions()
    {
        var response = await _clientA.GetAsync("/api/dashboard?period=this-month");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        body!.ActionBand.PendingByPlanItems.Should().BeEmpty(
            "empty tenant has no pending transactions");
    }

    [Fact]
    public async Task GetDashboard_PendingByPlanItems_IsTenantScoped()
    {
        var payeeId = await CreatePayeeAsync(_clientA, "DASH-PEND-MT-001");
        var planId = await CreateActivePlanAsync(_clientA, "EUR");
        await CreateAssignmentAsync(_clientA, payeeId, planId);
        await CreateTransactionAsync(_clientA, payeeId, planId, amount: 1_000m, currency: "EUR",
            processImmediately: false);

        var bodyA = await (await _clientA.GetAsync("/api/dashboard?period=all-time"))
            .Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        var bodyB = await (await _clientB.GetAsync("/api/dashboard?period=all-time"))
            .Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        bodyA!.ActionBand.PendingByPlanItems.Should().HaveCount(1,
            "Tenant A has 1 plan with a pending transaction");
        bodyA.ActionBand.PendingByPlanItems[0].PendingCount.Should().Be(1);
        bodyA.ActionBand.PendingByPlanItems[0].Currency.Should().Be("EUR");

        bodyB!.ActionBand.PendingByPlanItems.Should().BeEmpty(
            "Tenant B has no pending transactions");
    }

    [Fact]
    public async Task GetDashboard_PendingByPlanItems_TwoPlans_CountsAreNotCartesianMultiplied()
    {
        // 2 plans, 1 payee assigned to each, 1 pending tx per plan — expect count=1 per plan.
        var payeeId1 = await CreatePayeeAsync(_clientA, "DASH-PEND-AC-001");
        var payeeId2 = await CreatePayeeAsync(_clientA, "DASH-PEND-AC-002");
        var planId1 = await CreateActivePlanAsync(_clientA, "EUR");
        var planId2 = await CreateActivePlanAsync(_clientA, "EUR");

        await CreateAssignmentAsync(_clientA, payeeId1, planId1);
        await CreateAssignmentAsync(_clientA, payeeId2, planId2);

        await CreateTransactionAsync(_clientA, payeeId1, planId1, amount: 500m, currency: "EUR",
            processImmediately: false);
        await CreateTransactionAsync(_clientA, payeeId2, planId2, amount: 700m, currency: "EUR",
            processImmediately: false);

        var body = await (await _clientA.GetAsync("/api/dashboard?period=all-time"))
            .Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        body!.ActionBand.PendingByPlanItems.Should().HaveCount(2,
            "two plans each have 1 pending transaction");
        body.ActionBand.PendingByPlanItems.Should().AllSatisfy(item =>
            item.PendingCount.Should().Be(1,
                "each plan has exactly 1 pending tx — Cartesian would inflate this"));
    }

    // ── Action band — Pending Approval count matches list default (hideZero=true) ──

    [Fact]
    public async Task GetDashboard_ActionBand_PendingApprovalCount_ExcludesZeroAmountPayouts()
    {
        // Two payees, same plan. Only one gets a transaction (→ non-zero payout).
        // The other has an assignment but no credits → $0 payout is created by the engine.
        // Dashboard must count only the non-zero one — matching the payouts list default (hideZero=true).
        var payeeWithAmount = await CreatePayeeAsync(_clientA, "DASH-PPA-AMT-001");
        var payeeZero       = await CreatePayeeAsync(_clientA, "DASH-PPA-ZERO-001");
        var planId = await CreateActivePlanAsync(_clientA, "EUR"); // 5% flat rate

        await CreateAssignmentAsync(_clientA, payeeWithAmount, planId);
        await CreateAssignmentAsync(_clientA, payeeZero,       planId);

        // Only payeeWithAmount gets a transaction → €10,000 × 5% = €500 credit
        await CreateTransactionAsync(_clientA, payeeWithAmount, planId, amount: 10_000m, currency: "EUR");

        // Process pending (async Hangfire job) — poll for credit to appear (up to 5 s)
        var processReq = new { scope = 0, scopeId = planId };
        (await _clientA.PostAsJsonAsync("/api/transactions/process-pending", processReq))
            .EnsureSuccessStatusCode();

        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var cr = await _clientA.GetAsync("/api/credits?pageSize=1");
            var crBody = await cr.Content.ReadFromJsonAsync<JsonElement>();
            if (crBody.GetProperty("totalCount").GetInt32() > 0) break;
        }

        // Calculate pay run: creates 2 payouts — payeeWithAmount at €500, payeeZero at €0
        var calcReq = new { periodStart = "2026-01-01", periodEnd = "2026-12-31" };
        (await _clientA.PostAsJsonAsync("/api/pay-runs/calculate", calcReq))
            .EnsureSuccessStatusCode();

        var resp = await _clientA.GetAsync("/api/dashboard?period=all-time");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        body!.ActionBand.PayoutsPendingApprovalCount.Should().Be(1,
            "only non-zero Calculated payouts count — the $0 payout must be excluded " +
            "to match the payouts list default (hideZero=true)");
        body.ActionBand.PayoutsPendingApprovalByCurrency.Should().HaveCount(1);
        body.ActionBand.PayoutsPendingApprovalByCurrency[0].Currency.Should().Be("EUR");
        body.ActionBand.PayoutsPendingApprovalByCurrency[0].Amount
            .Should().BeApproximately(500m, 0.01m,
                "€10,000 × 5% flat rate = €500; the $0 payout contributes nothing to the total");
    }

    [Fact]
    public async Task GetDashboard_ActionBand_PayoutsPendingApprovalCount_IsTenantScoped()
    {
        // Seed 1 Calculated payout in Tenant A; Tenant B has none.
        var payeeA = await CreatePayeeAsync(_clientA, "DASH-PPA-MT-A01");
        var planA  = await CreateActivePlanAsync(_clientA, "EUR");
        await CreateAssignmentAsync(_clientA, payeeA, planA);
        await CreateTransactionAsync(_clientA, payeeA, planA, amount: 5_000m, currency: "EUR");

        (await _clientA.PostAsJsonAsync("/api/transactions/process-pending",
            new { scope = 0, scopeId = planA })).EnsureSuccessStatusCode();

        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var cr = await _clientA.GetAsync("/api/credits?pageSize=1");
            var crBody = await cr.Content.ReadFromJsonAsync<JsonElement>();
            if (crBody.GetProperty("totalCount").GetInt32() > 0) break;
        }

        (await _clientA.PostAsJsonAsync("/api/pay-runs/calculate",
            new { periodStart = "2026-01-01", periodEnd = "2026-06-30" })).EnsureSuccessStatusCode();

        var dashA = await (await _clientA.GetAsync("/api/dashboard?period=all-time"))
            .Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);
        var dashB = await (await _clientB.GetAsync("/api/dashboard?period=all-time"))
            .Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        dashA!.ActionBand.PayoutsPendingApprovalCount.Should().Be(1,
            "Tenant A created 1 non-zero Calculated payout");
        dashB!.ActionBand.PayoutsPendingApprovalCount.Should().Be(0,
            "Tenant B has no payouts — action band must not leak across tenants");
    }

    // ── Cancelled exclusion from KPIs ───────────────────────────────────────

    [Fact]
    public async Task GetDashboard_PeriodBand_ExcludesCancelledTransactionsFromCountAndVolume()
    {
        var payeeId = await CreatePayeeAsync(_clientA, "DASH-CNCL-001");

        var validTxId = await CreateTransactionAndGetIdAsync(_clientA, payeeId, amount: 1_000m, currency: "EUR");
        var cancelledTxId = await CreateTransactionAndGetIdAsync(_clientA, payeeId, amount: 9_000m, currency: "EUR");

        await VoidTransactionAsync(_clientA, cancelledTxId);

        var resp = await _clientA.GetAsync("/api/dashboard?period=ytd");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DashboardResponse>(JsonOptions);

        body!.PeriodBand.TransactionsCount.Should().Be(1,
            "the Cancelled transaction must not be counted in the period KPI");
        body.PeriodBand.TransactionsVolumeByCurrency.Should().HaveCount(1);
        body.PeriodBand.TransactionsVolumeByCurrency[0].Amount
            .Should().BeApproximately(1_000m, 0.01m,
                "only the €1,000 valid transaction contributes to volume; the €9,000 Cancelled tx must be excluded");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private async Task<Guid> CreatePayeeAsync(HttpClient client, string code)
    {
        var req = new
        {
            fullName = $"Dashboard Payee {code}",
            employeeCode = code,
            email = $"{code.ToLower().Replace("-", "")}@test.com",
            hireDate = "2024-01-01",
        };
        var resp = await client.PostAsJsonAsync("/api/payees", req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateActivePlanAsync(HttpClient client, string currency = "EUR")
    {
        var planReq = new
        {
            name = $"Dash Plan {Guid.NewGuid().ToString("N")[..6]}",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2026-12-31",
            currency,
        };
        var planResp = await client.PostAsJsonAsync("/api/plans", planReq);
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
            floor = (object?)null,
        };
        await client.PostAsJsonAsync($"/api/plans/{planId}/rules", ruleReq);
        (await client.PostAsync($"/api/plans/{planId}/activate", null)).EnsureSuccessStatusCode();
        return planId;
    }

    private async Task CreateAssignmentAsync(HttpClient client, Guid payeeId, Guid planId)
    {
        var req = new { planId, payeeId, effectiveStart = "2025-01-01", effectiveEnd = "2026-12-31" };
        (await client.PostAsJsonAsync("/api/assignments", req)).EnsureSuccessStatusCode();
    }

    private async Task CreateAndActivateQuotaAsync(
        HttpClient client, Guid payeeId, Guid planId, decimal target, string currency = "EUR")
    {
        var req = new
        {
            payeeId,
            planId,
            measurementType = 0,
            amount = target,
            currency,
            periodStart = "2026-01-01",
            periodEnd = "2026-12-31",
        };
        var resp = await client.PostAsJsonAsync("/api/quotas", req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var quotaId = body.GetProperty("id").GetGuid();
        (await client.PostAsync($"/api/quotas/{quotaId}/activate", null)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Ingest defaults to ProcessImmediately = true (IngestTransactionCommand.cs), which credits and
    /// marks the transaction Calculated on the spot. Tests about PENDING work must opt out, or the
    /// row they are asserting on never exists in the Pending state.
    /// </summary>
    private async Task CreateTransactionAsync(
        HttpClient client, Guid payeeId, Guid planId, decimal amount, string currency = "EUR",
        bool processImmediately = true)
    {
        var req = new
        {
            payeeId,
            referenceNumber = $"DASH-TX-{Guid.NewGuid().ToString("N")[..8]}",
            amount,
            currency,
            transactionDate = "2026-06-01",
            quantity = 1,
            processImmediately,
        };
        (await client.PostAsJsonAsync("/api/transactions", req)).EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateTransactionAndGetIdAsync(
        HttpClient client, Guid payeeId, decimal amount, string currency = "EUR")
    {
        var req = new
        {
            payeeId,
            referenceNumber = $"DASH-TX-{Guid.NewGuid().ToString("N")[..8]}",
            amount,
            currency,
            transactionDate = "2026-06-01",
            quantity = 1,
        };
        var resp = await client.PostAsJsonAsync("/api/transactions", req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task VoidTransactionAsync(HttpClient client, Guid txId)
    {
        var resp = await client.PostAsJsonAsync($"/api/transactions/{txId}/void",
            new { reason = "cancelled-exclusion audit test" });
        resp.EnsureSuccessStatusCode();
    }

    // ── Response DTOs ───────────────────────────────────────────────────────

    private sealed record DashboardResponse(
        string PeriodLabel,
        ActionBandResponse ActionBand,
        PeriodBandResponse PeriodBand,
        TrendBandResponse? TrendBand,
        object[] ActivityFeed);

    private sealed record ActionBandResponse(
        int DraftPayRunsCount,
        int PayoutsPendingApprovalCount,
        CurrencyTotalResponse[] PayoutsPendingApprovalByCurrency,
        CurrencyTotalResponse[] PayoutsApprovedUnpaidByCurrency,
        PlanPendingCountResponse[] PendingByPlanItems);

    private sealed record PlanPendingCountResponse(
        Guid PlanId, string PlanName, string Currency, int PendingCount);

    private sealed record PeriodBandResponse(
        int TransactionsCount,
        CurrencyTotalResponse[] TransactionsVolumeByCurrency,
        CurrencyTotalResponse[] PayoutsTotalByCurrency,
        int CreditsCount,
        CurrencyTotalResponse[] CreditsTotalByCurrency,
        decimal? AvgQuotaAttainmentPercent,
        int ActivePlansCount,
        int ActiveQuotasCount,
        int PayeesActiveCount,
        int PayeesInactiveCount);

    private sealed record TrendBandResponse(
        string CurrentPeriodLabel,
        string PriorPeriodLabel,
        object[] CommissionTrend);

    private sealed record CurrencyTotalResponse(decimal Amount, string Currency);
}
