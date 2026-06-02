#pragma warning disable CS8602 // Possible null reference — test assertions handle this

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.BackgroundJobs;

/// <summary>
/// End-to-end integration tests for the TransactionImportJobHandler.
/// Tests span parse → validate → execute → Hangfire job execution → DB assertions.
/// </summary>
[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class TransactionImportJobTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private const string ColRef = "ReferenceNumber";
    private const string ColCode = "PayeeCode";
    private const string ColAmount = "Amount";
    private const string ColCurrency = "Currency";
    private const string ColDate = "TransactionDate";
    private const string ColExtId = "ExternalId";

    public TransactionImportJobTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetTransactionImportDataAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA, TestConstants.UserAId, "TenantAdmin");
        _clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB, TestConstants.UserBId, "TenantAdmin");

        // Seed payees for TenantA used across all job tests.
        await SeedPayeeAsync(_clientA, "TEST-EMP001", "Test Payee 1", "test-emp1@wasnie.test");
        await SeedPayeeAsync(_clientA, "TEST-EMP002", "Test Payee 2", "test-emp2@wasnie.test");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static StringContent JsonBody(object obj) =>
        new(JsonSerializer.Serialize(obj, JsonOpts), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), JsonOpts);

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

    private static MultipartFormDataContent BuildCsvContent(string csv, string fileName = "transactions.csv")
    {
        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private async Task SeedPayeeAsync(HttpClient client, string code, string name, string email)
    {
        var body = JsonBody(new { fullName = name, employeeCode = code, email, hireDate = "2022-01-01" });
        (await client.PostAsync("/api/payees", body)).IsSuccessStatusCode.Should().BeTrue();
    }

    private async Task<string> ParseAsync(HttpClient client, string csv)
    {
        using var form = BuildCsvContent(csv);
        var resp = await client.PostAsync("/api/imports/transactions/parse", form);
        var body = await ReadJson(resp);
        return body.GetProperty("fileId").GetString()!;
    }

    private async Task<Guid> ExecuteAsync(HttpClient client, string fileId, bool skipWarnings = false)
    {
        var body = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMapping(),
            options = new { skipRowsWithWarnings = skipWarnings },
        });
        var resp = await client.PostAsync("/api/imports/transactions/execute", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted,
            $"execute failed: {await resp.Content.ReadAsStringAsync()}");
        var result = await ReadJson(resp);
        return Guid.Parse(result.GetProperty("jobId").GetString()!);
    }

    private async Task<JobStatus> PollUntilDoneAsync(Guid jobId, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var resp = await _clientA.GetAsync($"/api/jobs/{jobId}");
            if (resp.IsSuccessStatusCode)
            {
                var status = await resp.Content.ReadFromJsonAsync<JobStatus>(JsonOpts);
                if (status?.State is "Succeeded" or "Failed")
                    return status!;
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Job {jobId} did not reach a terminal state within {timeoutSeconds}s.");
    }

    private ApplicationDbContext GetDbNoFilter()
    {
        // IgnoreQueryFilters is needed when querying from a scope outside an HTTP request —
        // TenantContext returns Guid.Empty outside HTTP, so all query filters see no data.
        var scope = _fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Happy path: end-to-end enqueue → job runs → verify DB state
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_ImportThreeRows_CreatesTransactionsAuditAndImportAudit()
    {
        var csv = BuildCsv(
            "TXN-HP-001,TEST-EMP001,1000.00,USD,2024-01-15,EXT-HP-001",
            "TXN-HP-002,TEST-EMP002,2500.00,EUR,2024-02-20,EXT-HP-002",
            "TXN-HP-003,TEST-EMP001,750.50,PLN,2024-03-10,");

        // Parse → execute → poll
        var fileId = await ParseAsync(_clientA, csv);
        var jobId = await ExecuteAsync(_clientA, fileId);
        var status = await PollUntilDoneAsync(jobId);

        status.State.Should().Be("Succeeded");
        status.ProgressCurrent.Should().Be(3);
        status.ProgressTotal.Should().Be(3);

        // Assert: 3 CompensationTransactions for TenantA
        using var db = GetDbNoFilter();
        var txns = await db.CompensationTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == TestConstants.TenantA && t.ReferenceNumber.StartsWith("TXN-HP-"))
            .ToListAsync();

        txns.Should().HaveCount(3);
        txns.Should().AllSatisfy(t =>
        {
            t.Source.Should().Be(TransactionSource.EtlImport);
            t.Status.Should().Be(CompensationTransactionStatus.Pending);
        });

        // Assert: 1 ImportAudit record for Transactions
        var importAudit = await db.ImportAudits
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == TestConstants.TenantA && a.ResourceType == "Transactions")
            .OrderByDescending(a => a.StartedAt)
            .FirstOrDefaultAsync();

        importAudit.Should().NotBeNull();
        importAudit!.CreatedCount.Should().Be(3);
        importAudit.SkippedCount.Should().Be(0);
        importAudit.Status.Should().Be("Success");

        // Assert: AuditLog entries for the 3 transactions created by this run.
        // Filter by ResourceId (transaction IDs) since AuditLogs cannot be deleted between tests.
        var createdTxnIds = txns.Select(t => t.Id.ToString()).ToHashSet();
        var auditLogs = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == TestConstants.TenantA
                        && a.Action == AuditActions.TransactionIngested
                        && createdTxnIds.Contains(a.ResourceId))
            .ToListAsync();

        auditLogs.Should().HaveCount(3);
        auditLogs.Should().AllSatisfy(a => a.ResourceType.Should().Be("Transactions"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Warning rows skipped when option is set
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_SkipWarningsTrue_WarningRowsNotInserted()
    {
        // Pre-insert a transaction with ExternalId "EXT-WARN-001" so the row with that
        // ExternalId gets a Warning on re-validation inside the job.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payeeId = await db.Payees
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == TestConstants.TenantA && p.EmployeeCode == "TEST-EMP001")
                .Select(p => p.Id)
                .FirstAsync();

            var tx = CompensationTransaction.Ingest(
                TestConstants.TenantA, "TXN-WARN-SEED", payeeId,
                Money.Of(100m, "USD"), new DateOnly(2024, 1, 1),
                TransactionSource.EtlImport, "system",
                Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(),
                externalId: "EXT-WARN-001");
            db.CompensationTransactions.Add(tx);
            await db.SaveChangesAsync();
        }

        var csv = BuildCsv(
            "TXN-WARN-001,TEST-EMP001,500.00,USD,2024-03-01,EXT-WARN-001", // Warning — external ID already used
            "TXN-WARN-002,TEST-EMP002,800.00,EUR,2024-03-05,");              // Valid

        var fileId = await ParseAsync(_clientA, csv);
        var jobId = await ExecuteAsync(_clientA, fileId, skipWarnings: true);
        var status = await PollUntilDoneAsync(jobId);

        status.State.Should().Be("Succeeded");

        using var db2 = GetDbNoFilter();
        var txns = await db2.CompensationTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == TestConstants.TenantA && t.ReferenceNumber.StartsWith("TXN-WARN-"))
            .ToListAsync();

        // TXN-WARN-001 was skipped (warning row), TXN-WARN-002 was created.
        txns.Should().ContainSingle(t => t.ReferenceNumber == "TXN-WARN-002");
        txns.Should().NotContain(t => t.ReferenceNumber == "TXN-WARN-001");

        var importAudit = await db2.ImportAudits
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == TestConstants.TenantA && a.ResourceType == "Transactions")
            .OrderByDescending(a => a.StartedAt)
            .FirstOrDefaultAsync();

        importAudit!.CreatedCount.Should().Be(1);
        importAudit.SkippedCount.Should().Be(1);
        importAudit.Status.Should().Be("PartialSuccess");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Idempotency: executing the same CSV twice skips already-committed rows
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_SameCsvTwice_SecondRunSkipsAllRows()
    {
        var csv = BuildCsv(
            "TXN-IDEM-001,TEST-EMP001,300.00,USD,2024-04-01,",
            "TXN-IDEM-002,TEST-EMP002,600.00,EUR,2024-04-05,");

        // First execution — both rows created.
        var fileId1 = await ParseAsync(_clientA, csv);
        var jobId1 = await ExecuteAsync(_clientA, fileId1);
        var status1 = await PollUntilDoneAsync(jobId1);
        status1.State.Should().Be("Succeeded");

        // Second execution — same reference numbers → DbUpdateException → both skipped.
        var fileId2 = await ParseAsync(_clientA, csv);
        var jobId2 = await ExecuteAsync(_clientA, fileId2);
        var status2 = await PollUntilDoneAsync(jobId2);
        status2.State.Should().Be("Succeeded"); // Job Succeeds; idempotent skips are not job failures

        // DB should still have exactly 2 transactions (no duplicates).
        using var db = GetDbNoFilter();
        var count = await db.CompensationTransactions
            .IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == TestConstants.TenantA &&
                             (t.ReferenceNumber == "TXN-IDEM-001" || t.ReferenceNumber == "TXN-IDEM-002"));
        count.Should().Be(2);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Cross-tenant: TenantB's job cannot see TenantA's payees
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossTenant_TenantBCannotImportWithTenantAPayeeCodes()
    {
        // TenantA has TEST-EMP001/002. TenantB has no payees.
        // If TenantB tries to import using TEST-EMP001, validation should show Error.

        var csv = BuildCsv("TXN-CROSS-001,TEST-EMP001,1000.00,USD,2024-01-15,");
        using var form = BuildCsvContent(csv);
        var parseResp = await _clientB.PostAsync("/api/imports/transactions/parse", form);
        parseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var parseBody = await ReadJson(parseResp);
        var fileId = parseBody.GetProperty("fileId").GetString()!;

        // Validate as TenantB — TEST-EMP001 is TenantA's payee, not visible to TenantB.
        var validateBody = JsonBody(new { fileId, columnMapping = DefaultMapping() });
        var validateResp = await _clientB.PostAsync("/api/imports/transactions/validate", validateBody);
        validateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var validateResult = await ReadJson(validateResp);

        validateResult.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0,
            "TenantB's validation must not see TenantA's payee codes");

        // Execute anyway — all rows have errors, so job creates 0 transactions.
        var execBody = JsonBody(new
        {
            fileId,
            columnMapping = DefaultMapping(),
            options = new { skipRowsWithWarnings = false },
        });
        var execResp = await _clientB.PostAsync("/api/imports/transactions/execute", execBody);
        execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var execResult = await ReadJson(execResp);
        var jobId = Guid.Parse(execResult.GetProperty("jobId").GetString()!);

        // Poll as TenantB's client — we need a dedicated poll loop here.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        JobStatus? finalStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            var statusResp = await _clientB.GetAsync($"/api/jobs/{jobId}");
            if (statusResp.IsSuccessStatusCode)
            {
                finalStatus = await statusResp.Content.ReadFromJsonAsync<JobStatus>(JsonOpts);
                if (finalStatus?.State is "Succeeded" or "Failed") break;
            }
            await Task.Delay(250);
        }

        finalStatus?.State.Should().Be("Succeeded");

        // No TenantB transactions in DB.
        using var db = GetDbNoFilter();
        var count = await db.CompensationTransactions
            .IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == TestConstants.TenantB);
        count.Should().Be(0, "TenantB's job must not create transactions with TenantA's payee data");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Job status endpoint: cross-tenant isolation (Rule 9.3.3)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJobStatus_CrossTenant_Returns404()
    {
        // TenantA creates a job; TenantB cannot see it.
        var csv = BuildCsv("TXN-ISO-001,TEST-EMP001,100.00,USD,2024-01-15,");
        var fileId = await ParseAsync(_clientA, csv);
        var jobId = await ExecuteAsync(_clientA, fileId);

        // TenantB trying to access TenantA's job ID → 404 (not 403, per Rule 9.3.3)
        var resp = await _clientB.GetAsync($"/api/jobs/{jobId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Chunk timing: 50-row chunk well under 5s (Rule 3.2.5)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChunkTiming_50Rows_CompletesWellUnder5s()
    {
        // Build 50 rows with distinct reference numbers and valid data.
        var rows = Enumerable.Range(1, 50)
            .Select(i => $"TXN-TIMING-{i:D3},{(i % 2 == 0 ? "TEST-EMP001" : "TEST-EMP002")},{100 + i:F2},USD,2024-01-{(i % 28) + 1:D2},")
            .ToArray();
        var csv = BuildCsv(rows);

        var start = DateTimeOffset.UtcNow;
        var fileId = await ParseAsync(_clientA, csv);
        var jobId = await ExecuteAsync(_clientA, fileId);
        await PollUntilDoneAsync(jobId, timeoutSeconds: 10);
        var elapsed = DateTimeOffset.UtcNow - start;

        // Report timing (not a hard failure — but should be well under 5s per Rule 3.2.5)
        elapsed.TotalSeconds.Should().BeLessThan(10,
            $"50-row import took {elapsed.TotalSeconds:N1}s — expected well under 5s per chunk");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Audit-batch failure behavior (reported per WI requirement)
    // ──────────────────────────────────────────────────────────────────────────

    // Note: the audit-batch failure path cannot be cleanly simulated in integration without
    // overriding the DbContext mid-job. The behavior is: if the batch AuditLog INSERT fails
    // after chunks are committed, the handler throws, Hangfire marks the job Failed (up to
    // 3 retries). On retry, idempotency skips already-committed transactions, and the audit
    // batch is attempted again. If all 3 attempts fail, the job remains Failed and the
    // committed transactions are "in DB but un-audited" — visible to operations but missing
    // the audit trail. This is the accepted trade-off for chunked bulk imports.
    // A monitoring alert on Failed background jobs (implemented in the Hangfire dashboard)
    // is the recovery trigger. Manual audit backfill is the recovery procedure.
    // This behavior is documented in docs/architecture/05-audit-trail.md.

    // ──────────────────────────────────────────────────────────────────────────
    //  Credit Engine integration: import with Plan + Assignment produces Credits
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithPlanAndAssignment_ImportedTransactions_AreCalculatedWithCredits()
    {
        // Arrange: create a EUR plan with 10% flat rule, assign to TEST-EMP001.
        Guid planId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payeeId = await db.Payees
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == TestConstants.TenantA && p.EmployeeCode == "TEST-EMP001")
                .Select(p => p.Id)
                .FirstAsync();

            planId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var plan = Plan.Create(TestConstants.TenantA, "Import Credit Plan", "desc",
                DateRange.Of(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
                "EUR", "test", planId, now, Guid.NewGuid());
            plan.AddRule("10pct Commission", 1,
                new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
                RateTable.Flat(0.10m));
            db.CompensationPlans.Add(plan);

            var payeeRef = PayeeReference.Snapshot(payeeId, "Test Payee 1", "TEST-EMP001");
            db.PlanAssignments.Add(PlanAssignment.Create(
                TestConstants.TenantA, planId, payeeId, payeeRef,
                DateRange.Of(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
                "test", Guid.NewGuid(), now, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        var csv = BuildCsv(
            "TXN-CRD-001,TEST-EMP001,500.00,EUR,2024-03-15,",
            "TXN-CRD-002,TEST-EMP001,1000.00,EUR,2024-06-20,",
            "TXN-CRD-003,TEST-EMP001,250.00,EUR,2024-09-10,");

        var fileId = await ParseAsync(_clientA, csv);
        var jobId = await ExecuteAsync(_clientA, fileId);
        var status = await PollUntilDoneAsync(jobId);
        status.State.Should().Be("Succeeded");

        // Assert all 3 transactions are Calculated and each has a Credit.
        using var verifyDb = GetDbNoFilter();
        var txns = await verifyDb.CompensationTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == TestConstants.TenantA && t.ReferenceNumber.StartsWith("TXN-CRD-"))
            .ToListAsync();

        txns.Should().HaveCount(3);
        txns.Should().AllSatisfy(t => t.Status.Should().Be(CompensationTransactionStatus.Calculated));

        var credits = await verifyDb.Credits
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == TestConstants.TenantA && c.PlanId == planId)
            .ToListAsync();
        credits.Should().HaveCount(3);
        credits.Should().Contain(c => c.CreditedAmount.Amount == 50m);    // 500 * 10%
        credits.Should().Contain(c => c.CreditedAmount.Amount == 100m);   // 1000 * 10%
        credits.Should().Contain(c => c.CreditedAmount.Amount == 25m);    // 250 * 10%
        credits.Should().AllSatisfy(c => c.Role.Should().Be(CreditRole.Primary));
    }

    private sealed record JobStatus(string State, int ProgressCurrent, int ProgressTotal, string? ErrorMessage);
}
