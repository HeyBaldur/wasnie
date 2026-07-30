using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Helpers;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Assignments;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class AssignmentsEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public AssignmentsEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetCompensationDataAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public async Task DisposeAsync() => await _fixture.ResetCompensationDataAsync();

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAssignments_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/assignments");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateAssignment_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/assignments", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── List Assignments ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAssignments_HappyPath_Returns200WithPagedResult()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        await CreateAssignmentAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.GetAsync("/api/assignments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<AssignmentSummaryResponse>();
        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListAssignments_Pagination_ReturnsCorrectPage()
    {
        var plan = await CreateActivePlanAsync(_clientA);
        for (var i = 0; i < 5; i++)
        {
            var payee = await CreatePayeeAsync(_clientA, $"EMP{i:D3}");
            await CreateAssignmentAsync(_clientA, payee.Id, plan.Id);
        }

        var response = await _clientA.GetAsync("/api/assignments?page=2&pageSize=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<AssignmentSummaryResponse>();
        result.Page.Should().Be(2);
        result.TotalCount.Should().Be(5);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAssignmentsByPayee_ReturnsOnlyThatPayeesAssignments()
    {
        var payee1 = await CreatePayeeAsync(_clientA, "EMP001");
        var payee2 = await CreatePayeeAsync(_clientA, "EMP002");
        var plan = await CreateActivePlanAsync(_clientA);
        await CreateAssignmentAsync(_clientA, payee1.Id, plan.Id);
        await CreateAssignmentAsync(_clientA, payee2.Id, plan.Id);

        var response = await _clientA.GetAsync($"/api/assignments/payee/{payee1.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<AssignmentSummaryResponse>();
        result.Items.Should().ContainSingle(a => a.PayeeId == payee1.Id);
        result.Items.Should().NotContain(a => a.PayeeId == payee2.Id);
    }

    // ── Create Assignment ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAssignment_ValidRequest_Returns200()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            payeeId = payee.Id,
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/assignments", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AssignmentSummaryResponse>();
        body!.Id.Should().NotBeEmpty();
        body.PayeeId.Should().Be(payee.Id);
        body.PlanId.Should().Be(plan.Id);
    }

    // ── An archived plan is retired and cannot gain a new assignment ──────────
    // Archiving already deactivates the assignments that exist at that moment, but that sweep cannot
    // cover one created afterwards — and the engine used to check only the assignment's status. On
    // 2026-07-22 an assignment that outlived its plan's archiving let the CRM sync allocate €25,560
    // against a retired plan. This is the write-time half of the guard; the read-time half lives in
    // PlanAssignmentResolver (see PlanAttributionTests).

    [Fact]
    public async Task CreateAssignment_ArchivedPlan_Returns400AndWritesNothing()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var archiveResp = await _clientA.PostAsync($"/api/plans/{plan.Id}/archive", null);
        archiveResp.EnsureSuccessStatusCode();

        var response = await _clientA.PostAsJsonAsync("/api/assignments", new
        {
            planId = plan.Id,
            payeeId = payee.Id,
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("archived");

        // Nothing was persisted: the payee has no assignment to the retired plan.
        var list = await _clientA.GetAsync($"/api/assignments/payee/{payee.Id}");
        var result = await list.Content.ReadPagedResultAsync<AssignmentSummaryResponse>();
        result.Items.Should().NotContain(a => a.PlanId == plan.Id);
    }

    [Fact]
    public async Task CreateAssignment_DraftPlan_StillWorks()
    {
        // Unchanged on purpose: a plan is normally assigned while being prepared and activated after.
        // Only Archived is a closed door — narrowing Draft too would break the ordinary setup order.
        var payee = await CreatePayeeAsync(_clientA);

        var planResp = await _clientA.PostAsJsonAsync("/api/plans", new
        {
            name = $"Draft Plan {Guid.NewGuid().ToString("N")[..8]}",
            description = "",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
        });
        planResp.EnsureSuccessStatusCode();
        var draft = (await planResp.Content.ReadFromJsonAsync<PlanResponse>())!;

        var response = await _clientA.PostAsJsonAsync("/api/assignments", new
        {
            planId = draft.Id,
            payeeId = payee.Id,
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateAssignment_EndBeforeStart_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            payeeId = payee.Id,
            effectiveStart = "2025-12-31",
            effectiveEnd = "2025-01-01"
        };

        var response = await _clientA.PostAsJsonAsync("/api/assignments", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAssignment_EmptyPlanId_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);

        var request = new
        {
            planId = Guid.Empty,
            payeeId = payee.Id,
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/assignments", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAssignment_PeriodMatchesPlan_Returns200()
    {
        // Plan effective period is 2025-01-01 .. 2025-12-31 (see CreateActivePlanAsync).
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            payeeId = payee.Id,
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31"
        };

        var response = await _clientA.PostAsJsonAsync("/api/assignments", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateAssignment_PeriodDoesNotMatchPlan_Returns400()
    {
        // Valid date range, but narrower than the plan's 2025-01-01 .. 2025-12-31 period.
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);

        var request = new
        {
            planId = plan.Id,
            payeeId = payee.Id,
            effectiveStart = "2025-02-01",
            effectiveEnd = "2025-11-30"
        };

        var response = await _clientA.PostAsJsonAsync("/api/assignments", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Update Notes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateNotes_ValidRequest_Returns204()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var assignment = await CreateAssignmentAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.PutAsJsonAsync(
            $"/api/assignments/{assignment.Id}/notes",
            new { notes = "Updated notes text." });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateNotes_NotesTooLong_Returns400()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var assignment = await CreateAssignmentAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.PutAsJsonAsync(
            $"/api/assignments/{assignment.Id}/notes",
            new { notes = new string('x', 2001) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Deactivate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAssignment_ActiveAssignment_Returns204()
    {
        var payee = await CreatePayeeAsync(_clientA);
        var plan = await CreateActivePlanAsync(_clientA);
        var assignment = await CreateAssignmentAsync(_clientA, payee.Id, plan.Id);

        var response = await _clientA.PostAsync($"/api/assignments/{assignment.Id}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task ListAssignments_CrossTenant_DoesNotLeak()
    {
        var payeeA = await CreatePayeeAsync(_clientA, "TA001");
        var planA = await CreateActivePlanAsync(_clientA);
        await CreateAssignmentAsync(_clientA, payeeA.Id, planA.Id);

        var payeeB = await CreatePayeeAsync(_clientB, "TB001");
        var planB = await CreateActivePlanAsync(_clientB);
        await CreateAssignmentAsync(_clientB, payeeB.Id, planB.Id);

        var response = await _clientA.GetAsync("/api/assignments");
        var result = await response.Content.ReadPagedResultAsync<AssignmentSummaryResponse>();

        result.Items.Should().ContainSingle(a => a.PayeeEmployeeCode == "TA001");
        result.Items.Should().NotContain(a => a.PayeeEmployeeCode == "TB001");
    }

    [Fact]
    public async Task UpdateNotes_CrossTenant_Returns422OrNotFound()
    {
        var payeeA = await CreatePayeeAsync(_clientA);
        var planA = await CreateActivePlanAsync(_clientA);
        var assignmentA = await CreateAssignmentAsync(_clientA, payeeA.Id, planA.Id);

        // TODO: F-028 — current impl returns 422 (UnprocessableEntity) instead of 404 for cross-tenant mutations.
        // Revisit when cross-tenant response standardization is completed.
        var response = await _clientB.PutAsJsonAsync(
            $"/api/assignments/{assignmentA.Id}/notes",
            new { notes = "Injected by tenant B" });

        new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound }
            .Should().Contain(response.StatusCode);
    }

    [Fact]
    public async Task DeactivateAssignment_CrossTenant_Returns422OrNotFound()
    {
        var payeeA = await CreatePayeeAsync(_clientA);
        var planA = await CreateActivePlanAsync(_clientA);
        var assignmentA = await CreateAssignmentAsync(_clientA, payeeA.Id, planA.Id);

        // TODO: F-028 — current impl returns 422 (UnprocessableEntity) instead of 404 for cross-tenant mutations.
        var response = await _clientB.PostAsync($"/api/assignments/{assignmentA.Id}/deactivate", null);

        new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound }
            .Should().Contain(response.StatusCode);
    }

    [Fact]
    public async Task CreateAssignment_CrossTenantPayee_Returns400OrNotFound()
    {
        var payeeA = await CreatePayeeAsync(_clientA, "TA002");
        var planB = await CreateActivePlanAsync(_clientB);

        // Tenant B tries to assign tenant A's payee
        var request = new
        {
            planId = planB.Id,
            payeeId = payeeA.Id,
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31"
        };

        // TODO: F-028 — current impl may return 400 or 422 instead of 404 for cross-tenant assignments.
        var response = await _clientB.PostAsJsonAsync("/api/assignments", request);

        new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound }
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

    private async Task<AssignmentSummaryResponse> CreateAssignmentAsync(
        HttpClient client, Guid payeeId, Guid planId)
    {
        var request = new
        {
            planId,
            payeeId,
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31"
        };
        var response = await client.PostAsJsonAsync("/api/assignments", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssignmentSummaryResponse>())!;
    }

    private sealed record PayeeResponse(Guid Id, string FullName, string EmployeeCode, string StatusLabel);
    private sealed record PlanResponse(Guid Id, string Name, string Status, int Version);
    private sealed record AssignmentSummaryResponse(Guid Id, Guid PlanId, Guid PayeeId, string PayeeEmployeeCode, string Status);
}
