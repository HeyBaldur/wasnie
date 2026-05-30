using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Wasnie.Domain.Entities;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.MultiTenant;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class MultiTenantDefenseTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public MultiTenantDefenseTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetPayeesAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── F-018: Cross-tenant manager lookup is blocked ─────────────────────────

    [Fact]
    public async Task GetPayees_CrossTenantManagerId_ManagerNameIsNull()
    {
        // Create a manager in tenant B and a payee in tenant A
        var managerB = await CreatePayeeAsync(_clientB, "Manager B", "MGRB001");
        var payeeA = await CreatePayeeAsync(_clientA, "Report A", "REPA001");

        // Use raw SQL to forge a cross-tenant ManagerId reference
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE Payees SET ManagerId = {managerB.Id} WHERE Id = {payeeA.Id}");

        // Fetch payees for tenant A — the manager from tenant B must not be resolved
        var response = await _clientA.GetAsync("/api/payees");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();
        body.Should().NotBeNull();
        var returned = body!.Items.SingleOrDefault(p => p.Id == payeeA.Id);
        returned.Should().NotBeNull();
        returned!.ManagerName.Should().BeNull("cross-tenant managers must not be resolved");
    }

    // ── F-019: ImportAudit global query filter is enforced ────────────────────

    [Fact]
    public async Task ImportAudit_GlobalQueryFilter_IsolatesByTenant()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var now = DateTimeOffset.UtcNow;

        await using var dbA = new ApplicationDbContext(
            options, new FixedTenantContext(TestConstants.TenantA), NoOpPublisher.Instance);

        dbA.ImportAudits.Add(ImportAudit.Create(
            Guid.NewGuid(), TestConstants.TenantA, "admin", now, now, "Payees", 1, 1, 0, "a.csv", "Completed"));
        await dbA.SaveChangesAsync();

        // TenantB context — query filter should return 0 records from TenantA data
        await using var dbB = new ApplicationDbContext(
            options, new FixedTenantContext(TestConstants.TenantB), NoOpPublisher.Instance);

        var visibleToB = await dbB.ImportAudits.ToListAsync();
        visibleToB.Should().BeEmpty("tenant B must not see tenant A's import audits");

        var visibleToA = await dbA.ImportAudits.ToListAsync();
        visibleToA.Should().HaveCount(1, "tenant A must see its own import audit");
    }

    // ── F-022: Missing or malformed tenant_id claim returns 401 ──────────────

    [Fact]
    public async Task GetPayees_WithNoTenantIdClaim_Returns401()
    {
        var token = GenerateTokenWithoutTenantId();
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/payees");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPayees_WithEmptyTenantIdClaim_Returns401()
    {
        var token = GenerateTokenWithTenantId(string.Empty);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/payees");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPayees_WithMalformedTenantIdClaim_Returns401()
    {
        var token = GenerateTokenWithTenantId("not-a-valid-guid");
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/payees");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GenerateTokenWithoutTenantId()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, TestConstants.UserAId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateTokenWithTenantId(string tenantIdValue)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, TestConstants.UserAId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantIdValue),
        };

        var token = new JwtSecurityToken(
            issuer: TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<PayeeResponse> CreatePayeeAsync(HttpClient client, string fullName, string employeeCode)
    {
        var request = new
        {
            fullName,
            employeeCode,
            email = $"{employeeCode.ToLower()}@test.com",
            hireDate = "2024-01-01",
        };
        var response = await client.PostAsJsonAsync("/api/payees", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayeeResponse>())!;
    }

    private sealed record PayeeResponse(
        Guid Id, string FullName, string EmployeeCode, string? ManagerName, string StatusLabel);

    private sealed record PagedResponse<T>(
        List<T> Items, int TotalCount, int Page, int PageSize, int TotalPages, bool HasNextPage, bool HasPreviousPage);
}
