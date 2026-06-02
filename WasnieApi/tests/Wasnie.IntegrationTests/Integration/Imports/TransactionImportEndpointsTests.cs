#pragma warning disable CS8602 // Possible null reference — test assertions handle this

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Imports;

/// <summary>
/// Integration tests for the three transaction import HTTP endpoints:
/// POST /api/imports/transactions/parse | validate | execute
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TransactionImportEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _adminA = null!;   // TenantAdmin for TenantA
    private HttpClient _adminB = null!;   // TenantAdmin for TenantB
    private HttpClient _managerA = null!; // Manager for TenantA — no Transactions.Create

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Column names used in all transaction CSV fixtures.
    private const string ColRef = "ReferenceNumber";
    private const string ColCode = "PayeeCode";
    private const string ColAmount = "Amount";
    private const string ColCurrency = "Currency";
    private const string ColDate = "TransactionDate";
    private const string ColExtId = "ExternalId";

    public TransactionImportEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetTransactionImportDataAsync();

        _adminA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");
        _adminB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB, TestConstants.UserBId, "TenantAdmin");
        _managerA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "Manager");

        // Seed two payees for TenantA used in transaction rows.
        await SeedPayeeAsync(_adminA, "TEST-EMP001", "Test Payee 1", "test-emp1@wasnie.test");
        await SeedPayeeAsync(_adminA, "TEST-EMP002", "Test Payee 2", "test-emp2@wasnie.test");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static MultipartFormDataContent BuildCsvContent(string csv, string fileName = "transactions.csv")
    {
        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static StringContent JsonBody(object obj) =>
        new(JsonSerializer.Serialize(obj, JsonOpts), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r)
    {
        var json = await r.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    private static object DefaultMapping() => new
    {
        ReferenceNumberColumn = ColRef,
        PayeeCodeColumn = ColCode,
        AmountColumn = ColAmount,
        CurrencyColumn = ColCurrency,
        TransactionDateColumn = ColDate,
        ExternalIdColumn = ColExtId,
    };

    private static string BuildCsv(params string[] dataRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{ColRef},{ColCode},{ColAmount},{ColCurrency},{ColDate},{ColExtId}");
        foreach (var row in dataRows)
            sb.AppendLine(row);
        return sb.ToString();
    }

    private async Task SeedPayeeAsync(HttpClient client, string code, string name, string email)
    {
        var body = JsonBody(new
        {
            fullName = name,
            employeeCode = code,
            email,
            hireDate = "2022-01-01",
        });
        var resp = await client.PostAsync("/api/payees", body);
        resp.IsSuccessStatusCode.Should().BeTrue($"seeding payee {code} failed: {await resp.Content.ReadAsStringAsync()}");
    }

    private async Task<string> ParseCsvAsync(HttpClient client, string csv, string fileName = "transactions.csv")
    {
        using var form = BuildCsvContent(csv, fileName);
        var resp = await client.PostAsync("/api/imports/transactions/parse", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"parse failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await ReadJson(resp);
        return body.GetProperty("fileId").GetString()!;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Parse endpoint
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parse_ValidCsv_Returns200WithHeadersAndRowCount()
    {
        var csv = BuildCsv(
            "TXN-001,TEST-EMP001,1000.00,USD,2024-01-15,EXT-001",
            "TXN-002,TEST-EMP002,2000.00,EUR,2024-02-20,");

        using var form = BuildCsvContent(csv);
        var resp = await _adminA.PostAsync("/api/imports/transactions/parse", form);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJson(resp);
        body.GetProperty("rowCount").GetInt32().Should().Be(2);
        body.GetProperty("fileId").GetString().Should().NotBeNullOrWhiteSpace();
        var headers = body.GetProperty("headers").EnumerateArray().Select(e => e.GetString()).ToList();
        headers.Should().Contain(ColRef).And.Contain(ColCode);
    }

    [Fact]
    public async Task Parse_NoAuthentication_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        using var form = BuildCsvContent(BuildCsv("TXN-001,TEST-EMP001,100,USD,2024-01-01,"));
        var resp = await anon.PostAsync("/api/imports/transactions/parse", form);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parse_NoTransactionsCreatePermission_Returns403()
    {
        using var form = BuildCsvContent(BuildCsv("TXN-001,TEST-EMP001,100,USD,2024-01-01,"));
        var resp = await _managerA.PostAsync("/api/imports/transactions/parse", form);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Parse_UnsupportedFormat_Returns400()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not a csv or xlsx"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(fileContent, "file", "data.txt");
        var resp = await _adminA.PostAsync("/api/imports/transactions/parse", content);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Validate endpoint
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_AllValidRows_Returns0Errors()
    {
        var csv = BuildCsv(
            "TXN-001,TEST-EMP001,1000.00,USD,2024-01-15,",
            "TXN-002,TEST-EMP002,500.00,EUR,2024-02-20,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadJson(resp);
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
        result.GetProperty("validRowCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Validate_EmptyPayeeCode_WhenOptional_NoError()
    {
        // Decision D: Transaction.PayeeId defaults to Optional for all tenants.
        // An empty payeeCode is accepted — the transaction will be ingested as Unassigned.
        var csv = BuildCsv("TXN-001,,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);

        var result = await ReadJson(resp);
        result.GetProperty("errorCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Validate_UnknownPayeeCode_ReturnsError()
    {
        var csv = BuildCsv("TXN-001,UNKNOWN-CODE,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);

        var result = await ReadJson(resp);
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_DuplicateReferenceInFile_ReturnsError()
    {
        var csv = BuildCsv(
            "TXN-DUP,TEST-EMP001,1000.00,USD,2024-01-15,",
            "TXN-DUP,TEST-EMP001,2000.00,USD,2024-01-16,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);

        var result = await ReadJson(resp);
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Validate_BadAmount_ReturnsError()
    {
        var csv = BuildCsv("TXN-001,TEST-EMP001,not-a-number,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);

        var result = await ReadJson(resp);
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_BadCurrency_ReturnsError()
    {
        var csv = BuildCsv("TXN-001,TEST-EMP001,1000.00,XX,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);

        var result = await ReadJson(resp);
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_FutureDate_ReturnsError()
    {
        var csv = BuildCsv("TXN-001,TEST-EMP001,1000.00,USD,2099-01-01,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);

        var result = await ReadJson(resp);
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_ExpiredFileId_Returns400()
    {
        var body = JsonBody(new { fileId = "doesnotexist00000000000000000000", columnMapping = DefaultMapping() });
        var resp = await _adminA.PostAsync("/api/imports/transactions/validate", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validate_NoTransactionsCreatePermission_Returns403()
    {
        var csv = BuildCsv("TXN-001,TEST-EMP001,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _managerA.PostAsync("/api/imports/transactions/validate", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Validate_FileIdFromOtherTenant_Returns400()
    {
        // TenantA uploads a file; TenantB cannot access TenantA's cache entry.
        var csv = BuildCsv("TXN-001,TEST-EMP001,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var resp = await _adminB.PostAsync("/api/imports/transactions/validate", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Execute endpoint
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_ValidCsv_Returns202WithJobId()
    {
        var csv = BuildCsv("TXN-001,TEST-EMP001,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMapping(),
            options = new { skipRowsWithWarnings = false },
        });
        var resp = await _adminA.PostAsync("/api/imports/transactions/execute", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted); // 202
        var result = await ReadJson(resp);
        result.GetProperty("jobId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Execute_ClearsCache_SecondCallReturns400()
    {
        var csv = BuildCsv("TXN-EX001,TEST-EMP001,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var execBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMapping(),
            options = new { skipRowsWithWarnings = false },
        });

        var first = await _adminA.PostAsync("/api/imports/transactions/execute", execBody);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var second = await _adminA.PostAsync("/api/imports/transactions/execute", execBody);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_NoAuthentication_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        var body = JsonBody(new
        {
            fileId = "anyid",
            columnMapping = DefaultMapping(),
            options = new { skipRowsWithWarnings = false },
        });
        var resp = await anon.PostAsync("/api/imports/transactions/execute", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Execute_NoTransactionsCreatePermission_Returns403()
    {
        var csv = BuildCsv("TXN-001,TEST-EMP001,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMapping(),
            options = new { skipRowsWithWarnings = false },
        });
        var resp = await _managerA.PostAsync("/api/imports/transactions/execute", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Execute_FileIdFromOtherTenant_Returns400()
    {
        var csv = BuildCsv("TXN-001,TEST-EMP001,1000.00,USD,2024-01-15,");
        var fileId = await ParseCsvAsync(_adminA, csv);

        var body = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMapping(),
            options = new { skipRowsWithWarnings = false },
        });
        var resp = await _adminB.PostAsync("/api/imports/transactions/execute", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
