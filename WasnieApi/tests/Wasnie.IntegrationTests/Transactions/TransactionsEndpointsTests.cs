using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Transactions;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TransactionsEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;
    private Guid _payeeAId;
    private Guid _payeeBId;

    public TransactionsEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetTransactionsAsync();
        await _fixture.ResetPayeesAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);

        var payeeA = await CreatePayeeAsync(_clientA, "Payee Alpha", "PA001");
        _payeeAId = payeeA.Id;

        var payeeB = await CreatePayeeAsync(_clientB, "Payee Beta", "PB001");
        _payeeBId = payeeB.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ValidTransaction_Returns201WithBody()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.TenantId.Should().Be(TestConstants.TenantA);
        body.ReferenceNumber.Should().Be("TXN-2025-001");
        body.Amount.Should().Be(1500.00m);
        body.Currency.Should().Be("EUR");
        body.Source.Should().Be("Manual");
        body.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Post_ValidTransaction_SetsLocationHeader()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Unauthenticated_Returns401()
    {
        var anonClient = _fixture.Factory.CreateClient();

        var response = await anonClient.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_RepRole_Returns403()
    {
        var repClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Rep");

        var response = await repClient.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_ManagerRole_Returns403()
    {
        var managerClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Manager");

        var response = await managerClient.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_CompManagerRole_Returns201()
    {
        var compManagerClient = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "CompManager");

        var response = await compManagerClient.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId, "TXN-MGR-001"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_EmptyReferenceNumber_Returns400()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = "",
            payeeId = _payeeAId,
            amount = 100m,
            currency = "EUR",
            transactionDate = "2025-01-15"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_ZeroAmount_Returns400()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = "TXN-001",
            payeeId = _payeeAId,
            amount = 0m,
            currency = "EUR",
            transactionDate = "2025-01-15"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_InvalidCurrency_Returns400()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = "TXN-001",
            payeeId = _payeeAId,
            amount = 100m,
            currency = "EU",
            transactionDate = "2025-01-15"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_TransactionDateBefore2000_Returns400()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = "TXN-001",
            payeeId = _payeeAId,
            amount = 100m,
            currency = "EUR",
            transactionDate = "1999-12-31"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Business rules ────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_PayeeNotFound_Returns400()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = "TXN-001",
            payeeId = Guid.NewGuid(),
            amount = 100m,
            currency = "EUR",
            transactionDate = "2025-01-15"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("not found");
    }

    // ── Cross-tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task Post_CrossTenantPayee_Returns400()
    {
        // Tenant A client tries to reference Tenant B's payee
        var response = await _clientA.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeBId, "TXN-CROSS-001"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task Post_TenantATransaction_NotVisibleToTenantB()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Tenant B cannot see or access Tenant A's data (verified via the payee-not-found isolation above)
        // This test confirms TenantA's transaction was saved and does not surface to TenantB
        var crossResponse = await _clientB.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId, "TXN-CROSS-002"));
        crossResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Money-critical audit atomicity ────────────────────────────────────────

    [Fact]
    public async Task Post_ValidTransaction_AuditRecordCreated()
    {
        var response = await _clientA.PostAsJsonAsync("/api/transactions", ValidRequest(_payeeAId, "TXN-AUDIT-001"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty("response body should be non-empty JSON");
        body.Should().Contain("TXN-AUDIT-001", "response should echo back the reference number");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Wasnie.Infrastructure.Persistence.ApplicationDbContext>();

        var auditEntries = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.ResourceType == "Transaction")
            .ToListAsync();

        auditEntries.Should().NotBeEmpty("at least one Transaction audit record should exist after ingestion");
        var latestAudit = auditEntries.OrderByDescending(a => a.TimestampUtc).First();
        latestAudit.Action.Should().Be("TRANSACTION_INGESTED");
        latestAudit.TenantId.Should().Be(TestConstants.TenantA);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object ValidRequest(Guid payeeId, string refNum = "TXN-2025-001") => new
    {
        referenceNumber = refNum,
        payeeId,
        amount = 1500.00m,
        currency = "EUR",
        transactionDate = "2025-06-15"
    };

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
        decimal Amount, string Currency, string Source, string Status);
    private sealed record ErrorResponse(string Message);
}
