#pragma warning disable CS8602 // Possible null reference — test assertions handle this

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Fixtures;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Imports;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayeeImportEndpointsTests : IAsyncLifetime
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ImportFiles");

    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    static PayeeImportEndpointsTests()
    {
        GenerateImportFixtures.EnsureCreated(FixtureDir);
    }

    public PayeeImportEndpointsTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetImportsAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId);
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB, TestConstants.UserBId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static MultipartFormDataContent BuildFileContent(string filePath, string? overrideName = null)
    {
        var fileName = overrideName ?? Path.GetFileName(filePath);
        var bytes = File.ReadAllBytes(filePath);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static MultipartFormDataContent BuildInMemoryFileContent(byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static object DefaultMappingObject() => new
    {
        FullNameColumn = GenerateImportFixtures.ColFullName,
        EmployeeCodeColumn = GenerateImportFixtures.ColCode,
        EmailColumn = GenerateImportFixtures.ColEmail,
        HireDateColumn = GenerateImportFixtures.ColHireDate,
    };

    private static StringContent JsonBody(object obj) =>
        new(JsonSerializer.Serialize(obj, JsonOpts), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    // ──────────────────────────────────────────────────────────
    //  Parse endpoint
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Parse_ValidCsv_Returns200WithHeaders()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));

        var response = await _clientA.PostAsync("/api/imports/payees/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJson(response);
        body.GetProperty("headers").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("FullName").And.Contain("EmployeeCode");
    }

    [Fact]
    public async Task Parse_ValidXlsx_Returns200WithHeaders()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.xlsx"));

        var response = await _clientA.PostAsync("/api/imports/payees/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJson(response);
        body.GetProperty("headers").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("FullName").And.Contain("EmployeeCode");
    }

    [Fact]
    public async Task Parse_FileTooLarge_Returns400()
    {
        // 5.1 MB random bytes
        var bigFile = new byte[5 * 1024 * 1024 + 1024];
        new Random(42).NextBytes(bigFile);
        using var form = BuildInMemoryFileContent(bigFile, "huge.csv");

        var response = await _clientA.PostAsync("/api/imports/payees/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parse_TooManyRows_Returns400()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "too_many_rows.csv"));

        var response = await _clientA.PostAsync("/api/imports/payees/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parse_UnsupportedFormat_Returns400()
    {
        var txtContent = Encoding.UTF8.GetBytes("some plain text");
        using var form = BuildInMemoryFileContent(txtContent, "data.txt");

        var response = await _clientA.PostAsync("/api/imports/payees/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parse_NoAuthentication_Returns401()
    {
        var anonClient = _fixture.Factory.CreateClient();
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));

        var response = await anonClient.PostAsync("/api/imports/payees/parse", form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parse_ReturnsValidFileId()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));

        var response = await _clientA.PostAsync("/api/imports/payees/parse", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJson(response);
        var fileId = body.GetProperty("fileId").GetString();
        fileId.Should().NotBeNullOrWhiteSpace();
        // Cache stores Guid.NewGuid().ToString("N") — 32 hex chars, no hyphens
        fileId!.Length.Should().Be(32);
    }

    // ──────────────────────────────────────────────────────────
    //  Validate endpoint
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_AfterParse_AllValidRows_ReturnsZeroErrors()
    {
        // Parse first
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        // Validate
        var validateBody = JsonBody(new { fileId, columnMapping = DefaultMappingObject() });
        var response = await _clientA.PostAsync("/api/imports/payees/validate", validateBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJson(response);
        body.GetProperty("errorCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Validate_AfterParse_FileWithErrors_ReturnsErrorDetails()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_with_errors.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var validateBody = JsonBody(new { fileId, columnMapping = DefaultMappingObject() });
        var response = await _clientA.PostAsync("/api/imports/payees/validate", validateBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJson(response);
        body.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_UnknownFileId_Returns400()
    {
        // The controller returns 400 BadRequest (not 404) for missing fileId
        var validateBody = JsonBody(new { fileId = "nonexistentfileid000000000000000", columnMapping = DefaultMappingObject() });

        var response = await _clientA.PostAsync("/api/imports/payees/validate", validateBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validate_FileIdFromOtherTenant_Returns400()
    {
        // Cache keys are tenant-scoped: import:payees:{tenantId}:{fileId}.
        // TenantB cannot access a file uploaded by TenantA — the key does not exist in TenantB's namespace.
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var validateBody = JsonBody(new { fileId, columnMapping = DefaultMappingObject() });
        var response = await _clientB.PostAsync("/api/imports/payees/validate", validateBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_FileIdFromOtherTenant_Returns400()
    {
        // Cache keys are tenant-scoped: TenantB cannot execute an import uploaded by TenantA.
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var executeBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });
        var response = await _clientB.PostAsync("/api/imports/payees/execute", executeBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────
    //  Execute endpoint
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_AllValid_Returns200WithCreatedCount()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var executeBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });
        var response = await _clientA.PostAsync("/api/imports/payees/execute", executeBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJson(response);
        body.GetProperty("createdCount").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task Execute_AllValid_PayeesVisibleViaPayeesApi()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var executeBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });
        await _clientA.PostAsync("/api/imports/payees/execute", executeBody);

        var listResp = await _clientA.GetAsync("/api/payees?pageNumber=1&pageSize=50");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await ReadJson(listResp);
        // The list should contain items
        listBody.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task Execute_UnknownFileId_Returns400()
    {
        var executeBody = JsonBody(new
        {
            fileId = "nonexistentfileid000000000000000",
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });

        var response = await _clientA.PostAsync("/api/imports/payees/execute", executeBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_SameFileIdTwice_SecondCallReturns400()
    {
        // First parse
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var executeBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });

        // First execute: removes from cache
        var firstResp = await _clientA.PostAsync("/api/imports/payees/execute", executeBody);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second execute: cache entry gone → 400
        var secondResp = await _clientA.PostAsync("/api/imports/payees/execute", executeBody);
        secondResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_NoAuthentication_Returns401()
    {
        var anonClient = _fixture.Factory.CreateClient();
        var executeBody = JsonBody(new
        {
            fileId = "anyid",
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });

        var response = await anonClient.PostAsync("/api/imports/payees/execute", executeBody);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ──────────────────────────────────────────────────────────
    //  Audit trail
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_Successful_ImportAuditCreatedInDb()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var executeBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });
        await _clientA.PostAsync("/api/imports/payees/execute", executeBody);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = db.ImportAudits.IgnoreQueryFilters().ToList();
        audits.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Execute_AuditRecord_HasCorrectTenantAndFileName()
    {
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        var executeBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });
        await _clientA.PostAsync("/api/imports/payees/execute", executeBody);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = db.ImportAudits.IgnoreQueryFilters().OrderByDescending(a => a.StartedAt).First();
        audit.TenantId.Should().Be(TestConstants.TenantA);
        audit.OriginalFileName.Should().Be("valid_10_payees.csv");
    }

    // ──────────────────────────────────────────────────────────
    //  End-to-end flow
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FullFlow_ParseValidateThenExecute_CreatesAllPayees()
    {
        // Step 1: Parse
        using var form = BuildFileContent(Path.Combine(FixtureDir, "valid_10_payees.csv"));
        var parseResp = await _clientA.PostAsync("/api/imports/payees/parse", form);
        parseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;
        parseBody.GetProperty("rowCount").GetInt32().Should().Be(10);

        // Step 2: Validate — expect 0 errors
        var validateBody = JsonBody(new { fileId, columnMapping = DefaultMappingObject() });
        var validateResp = await _clientA.PostAsync("/api/imports/payees/validate", validateBody);
        validateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var validateRespBody = await ReadJson(validateResp);
        validateRespBody.GetProperty("errorCount").GetInt32().Should().Be(0);

        // Step 3: Execute
        var executeBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMappingObject(),
            options = new { skipRowsWithWarnings = false },
        });
        var executeResp = await _clientA.PostAsync("/api/imports/payees/execute", executeBody);
        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeRespBody = await ReadJson(executeResp);
        executeRespBody.GetProperty("createdCount").GetInt32().Should().Be(10);

        // Step 4: Confirm via /api/payees
        var listResp = await _clientA.GetAsync("/api/payees?pageNumber=1&pageSize=50");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await ReadJson(listResp);
        listBody.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(10);
    }
}
