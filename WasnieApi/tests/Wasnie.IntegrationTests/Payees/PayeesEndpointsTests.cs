using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Payees;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeesEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    public PayeesEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetPayeesAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Default pagination ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayees_WithDefaultPagination_ReturnsFirst25()
    {
        // Create 30 payees for tenant A
        for (int i = 1; i <= 30; i++)
            await CreatePayeeAsync(_clientA, $"Payee {i:D2}", $"EMP{i:D3}");

        var response = await _clientA.GetAsync("/api/payees");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();
        body.Should().NotBeNull();
        body!.PageSize.Should().Be(25);
        body.Page.Should().Be(1);
        body.TotalCount.Should().Be(30);
        body.Items.Should().HaveCount(25);
        body.HasNextPage.Should().BeTrue();
        body.HasPreviousPage.Should().BeFalse();
    }

    // ── Page 2 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayees_WithPage2_ReturnsCorrectItems()
    {
        for (int i = 1; i <= 30; i++)
            await CreatePayeeAsync(_clientA, $"Alpha {i:D2}", $"A{i:D3}");

        var response = await _clientA.GetAsync("/api/payees?page=2&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();
        body.Should().NotBeNull();
        body!.Page.Should().Be(2);
        body.PageSize.Should().Be(10);
        body.Items.Should().HaveCount(10);
        body.TotalCount.Should().Be(30);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayees_WithSearch_FiltersResults()
    {
        await CreatePayeeAsync(_clientA, "Alice Wonderland", "EMP001");
        await CreatePayeeAsync(_clientA, "Bob Builder", "EMP002");
        await CreatePayeeAsync(_clientA, "Charlie Chaplin", "EMP003");

        var response = await _clientA.GetAsync("/api/payees?search=alice");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();
        body.Should().NotBeNull();
        body!.Items.Should().ContainSingle();
        body.Items[0].FullName.Should().Be("Alice Wonderland");
    }

    // ── Status filter ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayees_WithStatusFilter_OnlyReturnsMatching()
    {
        var payee = await CreatePayeeAsync(_clientA, "Active Person", "ACT001");
        await CreatePayeeAsync(_clientA, "Another Person", "ANO001");

        // Mark first payee as on-leave via the API
        await _clientA.PostAsync($"/api/payees/{payee.Id}/mark-on-leave", null);

        // Filter for status=1 (OnLeave)
        var response = await _clientA.GetAsync("/api/payees?filters[status]=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();
        body.Should().NotBeNull();
        body!.Items.Should().ContainSingle();
        body.Items[0].FullName.Should().Be("Active Person");
    }

    // ── Sort by full name desc ────────────────────────────────────────────────

    [Fact]
    public async Task GetPayees_WithSortByFullNameDesc_ReturnsInDescOrder()
    {
        await CreatePayeeAsync(_clientA, "Zebra Zane", "Z001");
        await CreatePayeeAsync(_clientA, "Alpha Apple", "A001");
        await CreatePayeeAsync(_clientA, "Middle Man", "M001");

        var response = await _clientA.GetAsync("/api/payees?sortBy=fullname&sortOrder=desc");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCountGreaterThan(0);
        body.Items[0].FullName.Should().Be("Zebra Zane");
    }

    // ── Invalid sortBy falls back to default ─────────────────────────────────

    [Fact]
    public async Task GetPayees_WithInvalidSortBy_FallsBackToDefault()
    {
        await CreatePayeeAsync(_clientA, "Zebra Zane", "ZZ001");
        await CreatePayeeAsync(_clientA, "Alpha Apple", "AA001");

        var response = await _clientA.GetAsync("/api/payees?sortBy=nonexistentfield");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();
        body.Should().NotBeNull();
        // Default sort is fullname asc, so Alpha comes first
        body!.Items[0].FullName.Should().StartWith("Alpha");
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task GetPayees_CrossTenant_DoesNotLeak()
    {
        await CreatePayeeAsync(_clientA, "Tenant A Person", "TA001");
        await CreatePayeeAsync(_clientB, "Tenant B Person", "TB001");

        var responseA = await _clientA.GetAsync("/api/payees");
        var bodyA = await responseA.Content.ReadFromJsonAsync<PagedResponse<PayeeResponse>>();

        bodyA.Should().NotBeNull();
        bodyA!.Items.Should().Contain(p => p.FullName == "Tenant A Person");
        bodyA.Items.Should().NotContain(p => p.FullName == "Tenant B Person");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private sealed record PayeeResponse(Guid Id, string FullName, string EmployeeCode, string StatusLabel);
    private sealed record PagedResponse<T>(List<T> Items, int TotalCount, int Page, int PageSize, int TotalPages, bool HasNextPage, bool HasPreviousPage);
}
