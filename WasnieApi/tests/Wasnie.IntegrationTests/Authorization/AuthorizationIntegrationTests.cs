using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Authorization;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class AuthorizationIntegrationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _repClient = null!;
    private HttpClient _managerClient = null!;
    private HttpClient _adminClient = null!;

    public AuthorizationIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetPayeesAsync();
        _repClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "Rep");
        _managerClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "Manager");
        _adminClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Rep cannot create payees ──────────────────────────────────────────────

    [Fact]
    public async Task CreatePayee_Returns403_ForRepRole()
    {
        var response = await _repClient.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Rep Attempt",
            employeeCode = "REP001",
            email = "rep@test.com",
            hireDate = "2024-01-15",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Manager cannot create payees ─────────────────────────────────────────

    [Fact]
    public async Task CreatePayee_Returns403_ForManagerRole()
    {
        var response = await _managerClient.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Manager Attempt",
            employeeCode = "MGR001",
            email = "mgr@test.com",
            hireDate = "2024-01-15",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TenantAdmin can create payees ─────────────────────────────────────────

    [Fact]
    public async Task CreatePayee_Returns201_ForTenantAdmin()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Admin Payee",
            employeeCode = "ADMN001",
            email = "admin.payee@test.com",
            hireDate = "2024-01-15",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Forbidden response has structured error body ──────────────────────────

    [Fact]
    public async Task ForbiddenResponse_HasErrorField()
    {
        var response = await _repClient.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Rep Again",
            employeeCode = "REP002",
            email = "rep2@test.com",
            hireDate = "2024-01-15",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ForbiddenResponse>();
        body!.Error.Should().Be("Forbidden");
        body.Message.Should().NotBeNullOrEmpty();
    }

    // ── Rep cannot create plans ───────────────────────────────────────────────

    [Fact]
    public async Task CreatePlan_Returns403_ForRepRole()
    {
        var response = await _repClient.PostAsJsonAsync("/api/plans", new
        {
            name = "Rep Plan",
            description = "test",
            effectiveStart = "2025-01-01",
            effectiveEnd = "2025-12-31",
            currency = "EUR",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ForbiddenResponse(string Error, string Message);
}
