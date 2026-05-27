using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Audit;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Audit;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class AuditTrailIntegrationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public AuditTrailIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetPayeesAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── F-014: Payee creation produces an audit record ────────────────────────

    [Fact]
    public async Task CreatePayee_ProducesAuditLog_WithPayeeCreatedAction()
    {
        var response = await _clientA.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Audit Alice",
            employeeCode = "AUDIT001",
            email = "audit.alice@test.com",
            hireDate = "2024-01-15",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payee = await response.Content.ReadFromJsonAsync<PayeeIdResponse>();

        var logs = await GetAuditLogsForResourceAsync(payee!.Id.ToString(), ResourceTypes.Payee);
        logs.Should().ContainSingle(l => l.Action == AuditActions.PayeeCreated);
    }

    [Fact]
    public async Task CreatePayee_AuditLog_HasCorrectActorAndTenant()
    {
        var response = await _clientA.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Audit Bob",
            employeeCode = "AUDIT002",
            email = "audit.bob@test.com",
            hireDate = "2024-01-15",
        });
        response.EnsureSuccessStatusCode();
        var payee = await response.Content.ReadFromJsonAsync<PayeeIdResponse>();

        var logs = await GetAuditLogsForResourceAsync(payee!.Id.ToString(), ResourceTypes.Payee);
        var log = logs.Single(l => l.Action == AuditActions.PayeeCreated);
        log.TenantId.Should().Be(TestConstants.TenantA);
        log.ActorUserId.Should().Be(TestConstants.UserAId);
        log.ResourceId.Should().Be(payee.Id.ToString());
    }

    // ── F-014: Plan activate produces an audit record (via AuditBehavior) ─────

    [Fact]
    public async Task ActivatePlan_ProducesAuditLog_WithPlanActivatedAction()
    {
        var createResp = await _clientA.PostAsJsonAsync("/api/plans", new
        {
            name = "Audit Plan",
            description = "For audit test",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
        });
        createResp.EnsureSuccessStatusCode();
        var plan = await createResp.Content.ReadFromJsonAsync<PlanIdResponse>();

        var ruleResp = await _clientA.PostAsJsonAsync($"/api/plans/{plan!.Id}/rules", new
        {
            planId = plan.Id,
            name = "Base Commission",
            sortOrder = 1,
            measurement = new { _schema = 1, type = 0, sourceField = "amount", aggregation = 0 },
            rateTable = new { _schema = 1, type = 0, flatRate = 0.05 },
            trigger = (object?)null,
            modifier = (object?)null,
            cap = (object?)null,
            floor = (object?)null,
        });
        ruleResp.EnsureSuccessStatusCode();

        var activateResp = await _clientA.PostAsync($"/api/plans/{plan.Id}/activate", null);
        activateResp.EnsureSuccessStatusCode();

        var logs = await GetAuditLogsForResourceAsync(plan.Id.ToString(), ResourceTypes.Plan);
        logs.Should().ContainSingle(l =>
            l.Action == AuditActions.PlanActivated &&
            l.ResourceId == plan.Id.ToString());
    }

    // ── F-014: Audit records are tenant-isolated ──────────────────────────────

    [Fact]
    public async Task AuditLog_TenantIsolation_TenantBCannotSeeTenantALogs()
    {
        // Create a payee in tenant A
        var response = await _clientA.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Isolated Alice",
            employeeCode = "ISO001",
            email = "iso.alice@test.com",
            hireDate = "2024-01-15",
        });
        response.EnsureSuccessStatusCode();
        var payee = await response.Content.ReadFromJsonAsync<PayeeIdResponse>();

        // Query audit logs as tenant B — should not see tenant A's resource
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantBLogs = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == TestConstants.TenantB && l.ResourceId == payee!.Id.ToString())
            .ToListAsync();

        tenantBLogs.Should().BeEmpty("tenant B must not have audit logs for tenant A's resources");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<AuditLog>> GetAuditLogsForResourceAsync(string resourceId, string resourceType)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(l => l.ResourceId == resourceId && l.ResourceType == resourceType)
            .ToListAsync();
    }

    private sealed record PayeeIdResponse(Guid Id);
    private sealed record PlanIdResponse(Guid Id);
}
