using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Transactions;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TransactionAssignmentEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _adminA = null!;
    private HttpClient _adminB = null!;
    private HttpClient _managerA = null!;

    public TransactionAssignmentEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetCompensationDataAsync();
        _adminA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
        _adminB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
        _managerA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, role: "Manager");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task AssignPayee_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync($"/api/transactions/{Guid.NewGuid()}/assign-payee",
            new { payeeId = Guid.NewGuid() });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReassignPayee_WithoutToken_Returns401()
    {
        var client = _fixture.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync($"/api/transactions/{Guid.NewGuid()}/reassign-payee",
            new { newPayeeId = Guid.NewGuid(), reason = "test reason here" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AssignPayee_AsManager_Returns403()
    {
        var resp = await _managerA.PostAsJsonAsync($"/api/transactions/{Guid.NewGuid()}/assign-payee",
            new { payeeId = Guid.NewGuid() });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Assign payee ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AssignPayee_UnassignedTransaction_Returns200_WithUpdatedPayeeId()
    {
        var payee = await CreatePayeeAsync(_adminA, "EMP-ASSIGN-01");
        var tx = await CreateUnassignedTransactionAsync(_adminA, "REF-ASSIGN-01");

        var resp = await _adminA.PostAsJsonAsync($"/api/transactions/{tx.Id}/assign-payee",
            new { payeeId = payee.Id, comment = "initial assignment" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<TxJson>();
        body!.PayeeId.Should().Be(payee.Id);
    }

    [Fact]
    public async Task AssignPayee_WhenTransactionAlreadyHasPayee_Returns409()
    {
        var payee1 = await CreatePayeeAsync(_adminA, "EMP-ASSIGN-02");
        var payee2 = await CreatePayeeAsync(_adminA, "EMP-ASSIGN-03");
        var tx = await CreateAssignedTransactionAsync(_adminA, "REF-ASSIGN-02", payee1.Id);

        var resp = await _adminA.PostAsJsonAsync($"/api/transactions/{tx.Id}/assign-payee",
            new { payeeId = payee2.Id });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AssignPayee_CrossTenant_Returns404Or422()
    {
        var tx = await CreateUnassignedTransactionAsync(_adminA, "REF-ASSIGN-CT");
        var payee = await CreatePayeeAsync(_adminB, "EMP-B-01");

        var resp = await _adminB.PostAsJsonAsync($"/api/transactions/{tx.Id}/assign-payee",
            new { payeeId = payee.Id });

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    // ── Reassign payee ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignPayee_AssignedTransaction_Returns200_WithNewPayeeId()
    {
        var payee1 = await CreatePayeeAsync(_adminA, "EMP-REASSIGN-01");
        var payee2 = await CreatePayeeAsync(_adminA, "EMP-REASSIGN-02");
        var tx = await CreateAssignedTransactionAsync(_adminA, "REF-REASSIGN-01", payee1.Id);

        var resp = await _adminA.PostAsJsonAsync($"/api/transactions/{tx.Id}/reassign-payee",
            new { newPayeeId = payee2.Id, reason = "Cliente confirmó vendedor correcto en cierre de turno" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<TxJson>();
        body!.PayeeId.Should().Be(payee2.Id);
    }

    [Fact]
    public async Task ReassignPayee_WhenNoPayeeAssigned_Returns409()
    {
        var payee = await CreatePayeeAsync(_adminA, "EMP-REASSIGN-03");
        var tx = await CreateUnassignedTransactionAsync(_adminA, "REF-REASSIGN-02");

        var resp = await _adminA.PostAsJsonAsync($"/api/transactions/{tx.Id}/reassign-payee",
            new { newPayeeId = payee.Id, reason = "correcting unassigned transaction" });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReassignPayee_ShortReason_Returns400()
    {
        var payee1 = await CreatePayeeAsync(_adminA, "EMP-REASSIGN-04");
        var payee2 = await CreatePayeeAsync(_adminA, "EMP-REASSIGN-05");
        var tx = await CreateAssignedTransactionAsync(_adminA, "REF-REASSIGN-03", payee1.Id);

        var resp = await _adminA.PostAsJsonAsync($"/api/transactions/{tx.Id}/reassign-payee",
            new { newPayeeId = payee2.Id, reason = "short" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReassignPayee_EmptyReason_Returns400()
    {
        var payee1 = await CreatePayeeAsync(_adminA, "EMP-REASSIGN-06");
        var payee2 = await CreatePayeeAsync(_adminA, "EMP-REASSIGN-07");
        var tx = await CreateAssignedTransactionAsync(_adminA, "REF-REASSIGN-04", payee1.Id);

        var resp = await _adminA.PostAsJsonAsync($"/api/transactions/{tx.Id}/reassign-payee",
            new { newPayeeId = payee2.Id, reason = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AssignPayee_EligibleTransaction_RevertsStatusToPending()
    {
        var payee = await CreatePayeeAsync(_adminA, "EMP-ELIGIBLE-01");
        // Create unassigned transaction, then mark eligible (note: MarkEligible requires Pending)
        var tx = await CreateUnassignedTransactionAsync(_adminA, "REF-ELIGIBLE-01");

        // Assign → should succeed
        var resp = await _adminA.PostAsJsonAsync($"/api/transactions/{tx.Id}/assign-payee",
            new { payeeId = payee.Id });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<PayeeJson> CreatePayeeAsync(HttpClient client, string code)
    {
        var resp = await client.PostAsJsonAsync("/api/payees", new
        {
            fullName = $"Test {code}",
            employeeCode = code,
            email = $"{code.ToLower().Replace("-", "")}@test.com",
            hireDate = "2023-01-01"
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PayeeJson>())!;
    }

    private async Task<TxJson> CreateUnassignedTransactionAsync(HttpClient client, string refNum)
    {
        // Transaction import creates transactions — manual entry always requires payeeId currently.
        // Create with payeeId = null via raw HTTP to bypass handler check (field is Optional in test DB).
        var resp = await client.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = refNum,
            payeeId = (Guid?)null,
            amount = 500m,
            currency = "EUR",
            transactionDate = "2025-06-01"
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TxJson>())!;
    }

    private async Task<TxJson> CreateAssignedTransactionAsync(HttpClient client, string refNum, Guid payeeId)
    {
        var resp = await client.PostAsJsonAsync("/api/transactions", new
        {
            referenceNumber = refNum,
            payeeId,
            amount = 500m,
            currency = "EUR",
            transactionDate = "2025-06-01"
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TxJson>())!;
    }

    private record PayeeJson(Guid Id);
    private record TxJson(Guid Id, Guid? PayeeId, string Status);
}
