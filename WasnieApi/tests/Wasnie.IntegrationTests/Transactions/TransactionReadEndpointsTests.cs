using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Transactions;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TransactionReadEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;
    private Guid _payeeAId;
    private Guid _payeeBId;

    public TransactionReadEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetTransactionsAsync();
        await _fixture.ResetPayeesAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);

        var payeeA = await CreatePayeeAsync(_clientA, "Alpha Payee", "PA001");
        _payeeAId = payeeA.Id;

        var payeeB = await CreatePayeeAsync(_clientB, "Beta Payee", "PB001");
        _payeeBId = payeeB.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Default pagination ────────────────────────────────────────────────────

    [Fact]
    public async Task List_DefaultPagination_ReturnsFirst25Items()
    {
        for (int i = 1; i <= 30; i++)
            await CreateTransactionAsync(_clientA, _payeeAId, $"TXN-PAGE-{i:D3}", 100m + i);

        var response = await _clientA.GetAsync("/api/transactions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body.Should().NotBeNull();
        body!.PageSize.Should().Be(25);
        body.Page.Should().Be(1);
        body.TotalCount.Should().Be(30);
        body.Items.Should().HaveCount(25);
        body.HasNextPage.Should().BeTrue();
        body.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task List_Page2_ReturnsCorrectSubset()
    {
        for (int i = 1; i <= 30; i++)
            await CreateTransactionAsync(_clientA, _payeeAId, $"TXN-PG2-{i:D3}", 100m);

        var response = await _clientA.GetAsync("/api/transactions?page=2&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body.Should().NotBeNull();
        body!.Page.Should().Be(2);
        body.PageSize.Should().Be(10);
        body.Items.Should().HaveCount(10);
        body.TotalCount.Should().Be(30);
    }

    // ── PageSize clamp ────────────────────────────────────────────────────────

    [Fact]
    public async Task List_PageSizeOver100_Returns400()
    {
        var response = await _clientA.GetAsync("/api/transactions?pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Sort whitelisted fields ───────────────────────────────────────────────

    [Fact]
    public async Task List_SortByTransactionDate_Asc_ReturnsAscendingOrder()
    {
        // Create out-of-order: March, January, February
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-SORT-C", 100m, "2025-03-15");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-SORT-A", 100m, "2025-01-15");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-SORT-B", 100m, "2025-02-15");

        var response = await _clientA.GetAsync("/api/transactions?sortBy=transactionDate&sortOrder=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items.Should().HaveCount(3);
        body.Items[0].TransactionDate.Should().Be("2025-01-15");
        body.Items[1].TransactionDate.Should().Be("2025-02-15");
        body.Items[2].TransactionDate.Should().Be("2025-03-15");
    }

    [Fact]
    public async Task List_SortByTransactionDate_Desc_ReturnsDescendingOrder()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-DESC-A", 100m, "2025-01-15");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-DESC-B", 100m, "2025-02-15");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-DESC-C", 100m, "2025-03-15");

        var response = await _clientA.GetAsync("/api/transactions?sortBy=transactionDate&sortOrder=desc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items[0].TransactionDate.Should().Be("2025-03-15");
        body.Items[2].TransactionDate.Should().Be("2025-01-15");
    }

    [Fact]
    public async Task List_SortByReferenceNumber_Asc_ReturnsAlphabeticalOrder()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-REF-C", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-REF-A", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-REF-B", 100m);

        var response = await _clientA.GetAsync("/api/transactions?sortBy=referenceNumber&sortOrder=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items[0].ReferenceNumber.Should().Be("TXN-REF-A");
        body.Items[2].ReferenceNumber.Should().Be("TXN-REF-C");
    }

    [Fact]
    public async Task List_SortByAmount_Asc_ReturnsAscendingAmounts()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-AMT-500", 500m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-AMT-100", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-AMT-250", 250m);

        var response = await _clientA.GetAsync("/api/transactions?sortBy=amount&sortOrder=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items.Should().HaveCount(3);
        body.Items[0].Amount.Should().Be(100m);
        body.Items[2].Amount.Should().Be(500m);
    }

    [Fact]
    public async Task List_SortByStatus_Returns200WithCorrectCount()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-ST-1", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-ST-2", 200m);

        var response = await _clientA.GetAsync("/api/transactions?sortBy=status&sortOrder=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_SortByIngestedAt_Returns200WithCorrectCount()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-IA-1", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-IA-2", 200m);

        var response = await _clientA.GetAsync("/api/transactions?sortBy=ingestedAt&sortOrder=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_InvalidSortField_Returns400_NamingTheAllowedValues()
    {
        // Fail fast: an unknown sort field used to be normalised away silently, so a caller who
        // misspelled a column got a differently-ordered page and no way to notice.
        var response = await _clientA.GetAsync("/api/transactions?sortBy=nonexistentfield");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("nonexistentfield");
        body.Should().Contain("transactiondate", "the error must name what IS allowed");
    }

    [Fact]
    public async Task List_WithoutSortField_DefaultsToTransactionDateDescending()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-DEFSORT-Z", 100m, "2025-03-15");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-DEFSORT-A", 100m, "2025-01-15");

        var response = await _clientA.GetAsync("/api/transactions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        // Newest business event first — not newest ingestion.
        body!.Items[0].TransactionDate.Should().Be("2025-03-15");
    }

    [Fact]
    public async Task List_ValidSortField_StillSorts()
    {
        var response = await _clientA.GetAsync("/api/transactions?sortBy=amount&sortOrder=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items.Select(i => i.Amount).Should().BeInAscendingOrder();
    }

    // ── Filters ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-STATUS-1", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-STATUS-2", 200m);

        // All transactions are Pending after creation — filter by Pending returns all
        var response = await _clientA.GetAsync("/api/transactions?status=Pending");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(2);

        // Filter by Eligible (no transactions have this status yet) returns 0
        var emptyResponse = await _clientA.GetAsync("/api/transactions?status=Eligible");
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyBody = await emptyResponse.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        emptyBody!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_FilterByPayeeId_ReturnsOnlyThatPayeesTransactions()
    {
        var payeeC = await CreatePayeeAsync(_clientA, "Gamma Payee", "PC001");

        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-PAYEE-A1", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-PAYEE-A2", 200m);
        await CreateTransactionAsync(_clientA, payeeC.Id, "TXN-PAYEE-C1", 300m);

        var response = await _clientA.GetAsync($"/api/transactions?payeeId={_payeeAId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(2);
        body.Items.Should().AllSatisfy(t => t.PayeeId.Should().Be(_payeeAId));
    }

    [Fact]
    public async Task List_FilterByDateFrom_ReturnsOnlyOnOrAfterDate()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-FROM-OLD", 100m, "2024-12-31");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-FROM-NEW", 200m, "2025-06-15");

        var response = await _clientA.GetAsync("/api/transactions?dateFrom=2025-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(1);
        body.Items[0].ReferenceNumber.Should().Be("TXN-FROM-NEW");
    }

    [Fact]
    public async Task List_FilterByDateTo_ReturnsOnlyOnOrBeforeDate()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-TO-OLD", 100m, "2024-12-31");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-TO-NEW", 200m, "2025-06-15");

        var response = await _clientA.GetAsync("/api/transactions?dateTo=2025-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(1);
        body.Items[0].ReferenceNumber.Should().Be("TXN-TO-OLD");
    }

    [Fact]
    public async Task List_FilterByDateRange_ReturnsOnlyTransactionsInRange()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-RANGE-JAN", 100m, "2025-01-15");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-RANGE-MAR", 200m, "2025-03-15");
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-RANGE-JUN", 300m, "2025-06-15");

        var response = await _clientA.GetAsync("/api/transactions?dateFrom=2025-02-01&dateTo=2025-04-30");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(1);
        body.Items[0].ReferenceNumber.Should().Be("TXN-RANGE-MAR");
    }

    [Fact]
    public async Task List_FilterBySource_Manual_ReturnsAllApiCreatedTransactions()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-SRC-1", 100m);
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-SRC-2", 200m);

        var response = await _clientA.GetAsync("/api/transactions?source=Manual");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task List_FilterBySource_CrmSync_ReturnsZeroForManualTransactions()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-CRMSRC-1", 100m);

        var response = await _clientA.GetAsync("/api/transactions?source=CrmSync");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_CombinedFilterSortPagination_ReturnsCorrectResult()
    {
        // Create 5 transactions in January 2025
        for (int i = 1; i <= 5; i++)
            await CreateTransactionAsync(_clientA, _payeeAId, $"TXN-COMBO-{i:D2}", i * 100m, "2025-01-15");

        // 2 transactions outside the filter range
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-COMBO-OUT", 999m, "2025-06-15");

        var response = await _clientA.GetAsync(
            "/api/transactions?dateFrom=2025-01-01&dateTo=2025-01-31&sortBy=amount&sortOrder=desc&page=1&pageSize=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.TotalCount.Should().Be(5);
        body.Items.Should().HaveCount(3);
        body.Items[0].Amount.Should().Be(500m);
    }

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var anonClient = _fixture.Factory.CreateClient();
        var response = await anonClient.GetAsync("/api/transactions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_Unauthenticated_Returns401()
    {
        var anonClient = _fixture.Factory.CreateClient();
        var response = await anonClient.GetAsync($"/api/transactions/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ManagerRole_Returns403()
    {
        var managerClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Manager");
        var response = await managerClient.GetAsync("/api/transactions");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_RepRole_Returns403()
    {
        var repClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");
        var response = await repClient.GetAsync("/api/transactions");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_CompManagerRole_Returns200()
    {
        var compManagerClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");
        var response = await compManagerClient.GetAsync("/api/transactions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_ManagerRole_Returns403()
    {
        var managerClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Manager");
        var response = await managerClient.GetAsync($"/api/transactions/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Get by ID — happy path ────────────────────────────────────────────────

    [Fact]
    public async Task GetById_HappyPath_ReturnsTransactionWithCorrectFields()
    {
        var created = await CreateTransactionAsync(_clientA, _payeeAId, "TXN-GBY-001", 1500m, "2025-06-15");

        var response = await _clientA.GetAsync($"/api/transactions/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(created.Id);
        body.ReferenceNumber.Should().Be("TXN-GBY-001");
        body.Amount.Should().Be(1500m);
        body.Currency.Should().Be("EUR");
        body.Status.Should().Be("Pending");
        body.Source.Should().Be("Manual");
        body.PayeeId.Should().Be(_payeeAId);
    }

    [Fact]
    public async Task GetById_NonexistentId_Returns404()
    {
        var response = await _clientA.GetAsync($"/api/transactions/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task List_CrossTenant_NeverIncludesTenantBRows()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-CTLIST-A", 100m);
        await CreateTransactionAsync(_clientB, _payeeBId, "TXN-CTLIST-B", 200m);

        var response = await _clientA.GetAsync("/api/transactions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        body!.Items.Should().AllSatisfy(t => t.ReferenceNumber.Should().NotBe("TXN-CTLIST-B"));
        body.Items.Should().Contain(t => t.ReferenceNumber == "TXN-CTLIST-A");
    }

    [Fact]
    public async Task GetById_CrossTenant_Returns404NotForbidden()
    {
        // Create transaction as Tenant A
        var created = await CreateTransactionAsync(_clientA, _payeeAId, "TXN-CT-GBY-001", 100m);

        // Tenant B tries to access Tenant A's transaction
        var response = await _clientB.GetAsync($"/api/transactions/{created.Id}");

        // 404, not 403 — does not leak existence (Rule 9.3.3)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Payee name resolution ─────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsPayeeNameAndCode_ForTransactionsWithPayee()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-NAME-001", 500m);

        var response = await _clientA.GetAsync("/api/transactions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();
        var item = body!.Items.Single(t => t.ReferenceNumber == "TXN-NAME-001");
        item.PayeeName.Should().Be("Alpha Payee");
        item.PayeeEmployeeCode.Should().Be("PA001");
    }

    [Fact]
    public async Task List_PayeeName_NeverLeaksAcrossTenants()
    {
        await CreateTransactionAsync(_clientA, _payeeAId, "TXN-CT-NAME-A", 100m);
        await CreateTransactionAsync(_clientB, _payeeBId, "TXN-CT-NAME-B", 200m);

        var responseA = await _clientA.GetAsync("/api/transactions");
        var bodyA = await responseA.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>();

        bodyA!.Items.Should().AllSatisfy(t =>
            t.PayeeName.Should().NotBe("Beta Payee"));
        bodyA.Items.Should().Contain(t => t.PayeeName == "Alpha Payee");
    }

    // Regression: the detail screen showed "Unassigned" for a transaction the list showed as assigned,
    // because get-by-id returned PayeeId but not the enriched PayeeName/Code the list resolves.
    [Fact]
    public async Task GetById_ReturnsPayeeNameAndCode_ForTransactionWithPayee()
    {
        var created = await CreateTransactionAsync(_clientA, _payeeAId, "TXN-GBY-NAME-001", 500m);

        var response = await _clientA.GetAsync($"/api/transactions/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        body!.PayeeId.Should().Be(_payeeAId);
        body.PayeeName.Should().Be("Alpha Payee");
        body.PayeeEmployeeCode.Should().Be("PA001");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<TransactionResponse> CreateTransactionAsync(
        HttpClient client, Guid payeeId, string refNum, decimal amount, string transactionDate = "2025-06-15")
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = refNum,
            payeeId,
            amount,
            currency = "EUR",
            transactionDate
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>())!;
    }

    private async Task<PayeeResponse> CreatePayeeAsync(HttpClient client, string fullName, string employeeCode)
    {
        var response = await client.PostAsJsonAsync("/api/payees", new
        {
            fullName,
            employeeCode,
            email = $"{employeeCode.ToLower()}@test.com",
            hireDate = "2024-01-01"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayeeResponse>())!;
    }

    private sealed record PayeeResponse(Guid Id, string FullName);
    private sealed record TransactionResponse(
        Guid Id, Guid TenantId, string ReferenceNumber, Guid PayeeId,
        decimal Amount, string Currency, string TransactionDate, string Source, string Status,
        string? PayeeName = null, string? PayeeEmployeeCode = null);
    private sealed record PagedResponse<T>(
        List<T> Items, int TotalCount, int Page, int PageSize,
        int TotalPages, bool HasNextPage, bool HasPreviousPage);
}
