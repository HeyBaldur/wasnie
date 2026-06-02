using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Payees;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeeLifecycleEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _adminA = null!;
    private HttpClient _adminB = null!;
    private HttpClient _managerA = null!;

    public PayeeLifecycleEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetPayeesAsync();
        _adminA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _adminB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
        _managerA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Manager");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var resp = await client.PostAsync($"/api/payees/{Guid.NewGuid()}/deactivate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activate_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var resp = await client.PostAsync($"/api/payees/{Guid.NewGuid()}/activate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_AsManager_Returns403()
    {
        var payee = await CreatePayeeAsync(_adminA);
        var resp = await _managerA.PostAsync($"/api/payees/{payee.Id}/deactivate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_ActivePayee_Returns204_AndIsActiveBecomeFalse()
    {
        var payee = await CreatePayeeAsync(_adminA);

        var resp = await _adminA.PostAsync($"/api/payees/{payee.Id}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await _adminA.GetFromJsonAsync<PayeeJson>($"/api/payees/{payee.Id}");
        getResp!.IsActive.Should().BeFalse();
        getResp.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Activate_AfterDeactivation_Returns204_AndClearsDeactivatedAt()
    {
        var payee = await CreatePayeeAsync(_adminA);
        await _adminA.PostAsync($"/api/payees/{payee.Id}/deactivate", null);

        var resp = await _adminA.PostAsync($"/api/payees/{payee.Id}/activate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await _adminA.GetFromJsonAsync<PayeeJson>($"/api/payees/{payee.Id}");
        getResp!.IsActive.Should().BeTrue();
        getResp.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_IsIdempotent_Returns204()
    {
        var payee = await CreatePayeeAsync(_adminA);
        await _adminA.PostAsync($"/api/payees/{payee.Id}/deactivate", null);

        var resp = await _adminA.PostAsync($"/api/payees/{payee.Id}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_CrossTenant_PayeeFromOtherTenant_Returns422()
    {
        var payee = await CreatePayeeAsync(_adminA);
        var resp = await _adminB.PostAsync($"/api/payees/{payee.Id}/deactivate", null);
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<PayeeJson> CreatePayeeAsync(HttpClient client, string code = "EMP-LC-001")
    {
        var req = new
        {
            fullName = $"Lifecycle Test {code}",
            employeeCode = code,
            email = $"{code.ToLower().Replace("-", "")}@test.com",
            hireDate = "2023-01-01"
        };
        var resp = await client.PostAsJsonAsync("/api/payees", req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PayeeJson>())!;
    }

    private record PayeeJson(Guid Id, bool IsActive, DateTimeOffset? DeactivatedAt);
}
