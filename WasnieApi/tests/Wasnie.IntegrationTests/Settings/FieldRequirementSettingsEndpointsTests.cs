using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Settings;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class FieldRequirementSettingsEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _adminA = null!;
    private HttpClient _compManagerA = null!;
    private HttpClient _clientB = null!;

    public FieldRequirementSettingsEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Reset to defaults (Required = true for both fields in TenantA)
        await _fixture.ResetFieldRequirementSettingsAsync(TestConstants.TenantA);
        await _fixture.ResetFieldRequirementSettingsAsync(TestConstants.TenantB);
        _adminA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _compManagerA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── GET /api/settings/field-requirements ─────────────────────────────────

    [Fact]
    public async Task Get_AsTenantAdmin_ReturnsSettings()
    {
        var resp = await _adminA.GetAsync("/api/settings/field-requirements");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<List<FieldRequirementDto>>();
        body.Should().NotBeNull();
        body!.Should().HaveCount(2);
        body.Should().Contain(s => s.EntityName == "Payee" && s.FieldName == "Email" && s.IsRequired);
        body.Should().Contain(s => s.EntityName == "Payee" && s.FieldName == "HireDate" && s.IsRequired);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var resp = await _fixture.Factory.CreateClient().GetAsync("/api/settings/field-requirements");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_AsCompManager_Returns403()
    {
        var resp = await _compManagerA.GetAsync("/api/settings/field-requirements");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/settings/field-requirements/{entity}/{field} ────────────────

    [Fact]
    public async Task Put_EmailToOptional_Succeeds_And_GetReflectsChange()
    {
        var resp = await _adminA.PutAsJsonAsync(
            "/api/settings/field-requirements/Payee/Email",
            new { isRequired = false });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await _adminA.GetAsync("/api/settings/field-requirements");
        var body = await getResp.Content.ReadFromJsonAsync<List<FieldRequirementDto>>();
        body!.Single(s => s.FieldName == "Email").IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Put_UnknownField_Returns400()
    {
        var resp = await _adminA.PutAsJsonAsync(
            "/api/settings/field-requirements/Payee/NonExistentField",
            new { isRequired = false });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_AsCompManager_Returns403()
    {
        var resp = await _compManagerA.PutAsJsonAsync(
            "/api/settings/field-requirements/Payee/Email",
            new { isRequired = false });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task Put_TenantA_DoesNotAffectTenantB()
    {
        // Set TenantA Email to Optional
        await _adminA.PutAsJsonAsync(
            "/api/settings/field-requirements/Payee/Email",
            new { isRequired = false });

        // TenantB should still have Email as Required
        var resp = await _clientB.GetAsync("/api/settings/field-requirements");
        var body = await resp.Content.ReadFromJsonAsync<List<FieldRequirementDto>>();
        body!.Single(s => s.FieldName == "Email").IsRequired.Should().BeTrue();
    }

    // ── Payee creation respects settings ────────────────────────────────────────

    [Fact]
    public async Task CreatePayee_NullEmail_WhenEmailRequired_Returns400()
    {
        // Default: email required
        var resp = await _adminA.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Test User",
            employeeCode = "TEST-REQ-001",
            email = (string?)null,
            hireDate = "2020-01-01",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePayee_NullEmail_WhenEmailOptional_Returns201()
    {
        // Switch email to Optional
        await _adminA.PutAsJsonAsync("/api/settings/field-requirements/Payee/Email", new { isRequired = false });
        await _fixture.ResetPayeesAsync();

        var resp = await _adminA.PostAsJsonAsync("/api/payees", new
        {
            fullName = "No Email User",
            employeeCode = "TEST-OPT-001",
            email = (string?)null,
            hireDate = "2020-01-01",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreatePayee_InvalidEmailFormat_WhenOptional_Returns400()
    {
        await _adminA.PutAsJsonAsync("/api/settings/field-requirements/Payee/Email", new { isRequired = false });

        var resp = await _adminA.PostAsJsonAsync("/api/payees", new
        {
            fullName = "Bad Email User",
            employeeCode = "TEST-BAD-001",
            email = "not-an-email",
            hireDate = "2020-01-01",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePayee_NullHireDate_WhenHireDateOptional_Returns201()
    {
        await _adminA.PutAsJsonAsync("/api/settings/field-requirements/Payee/Email", new { isRequired = false });
        await _adminA.PutAsJsonAsync("/api/settings/field-requirements/Payee/HireDate", new { isRequired = false });
        await _fixture.ResetPayeesAsync();

        var resp = await _adminA.PostAsJsonAsync("/api/payees", new
        {
            fullName = "No HireDate User",
            employeeCode = "TEST-NH-001",
            email = (string?)null,
            hireDate = (string?)null,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed record FieldRequirementDto(string EntityName, string FieldName, bool IsRequired);
}
