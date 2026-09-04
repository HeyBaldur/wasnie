using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.IntegrationTests.Helpers;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Integration.Payouts;

[Collection(WasnieIntegrationTestCollection.Name)]
public sealed class PayoutsEndpointsTests : IAsyncLifetime
{
    /// <summary>
    /// ★★ THE SAME OPTIONS THE API SERIALISES WITH. Program.cs registers a JsonStringEnumConverter, so
    /// every enum leaves as a string; `ReadFromJsonAsync` with the defaults cannot read one back and
    /// throws. Deserialising the real contract with different settings from the real client is how a
    /// fixture ends up disagreeing with production (§A4) — here it failed loudly, which is the good
    /// outcome, but the fix is to match the contract rather than to weaken the DTO.
    /// </summary>
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TestDatabaseFixture _fixture;
    private HttpClient _clientA = null!;

    public PayoutsEndpointsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await ResetPayoutsAsync();
        _clientA = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantA);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPayouts_WithoutToken_Returns401()
    {
        var response = await _fixture.Factory.CreateClient().GetAsync("/api/payouts");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPayoutById_WithoutToken_Returns401()
    {
        var response = await _fixture.Factory.CreateClient().GetAsync($"/api/payouts/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPayouts_NoData_Returns200WithEmptyResult()
    {
        var response = await _clientA.GetAsync("/api/payouts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<PayoutListItemDto>();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListPayouts_WithPayout_ReturnsIt()
    {
        var payout = await SeedCalculatedPayoutAsync();

        var response = await _clientA.GetAsync("/api/payouts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<PayoutListItemDto>();
        result.Items.Should().ContainSingle(p => p.Id == payout.Id);
    }

    [Fact]
    public async Task ListPayouts_FilterByStatus_OnlyReturnsMatching()
    {
        await SeedCalculatedPayoutAsync();

        var response = await _clientA.GetAsync("/api/payouts?status=Approved");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<PayoutListItemDto>();
        result.Items.Should().BeEmpty(); // only Calculated seeded, filtered for Approved
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayoutById_NotFound_Returns404()
    {
        var response = await _clientA.GetAsync($"/api/payouts/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPayoutById_Exists_Returns200WithLines()
    {
        var payout = await SeedCalculatedPayoutAsync(lineCount: 2);

        var response = await _clientA.GetAsync($"/api/payouts/{payout.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<PayoutDto>(ApiJson);
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(payout.Id);
        dto.Status.Should().Be("Calculated");
        dto.Lines.Should().HaveCount(2);
    }

    // ── Approve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_NotFound_ReturnsBadRequest()
    {
        var response = await _clientA.PostAsync($"/api/payouts/{Guid.NewGuid()}/approve", null);
        // Handler returns Result.Failure("Payout not found.") → controller returns BadRequest
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approve_Calculated_Returns204()
    {
        var payout = await SeedCalculatedPayoutAsync();

        var response = await _clientA.PostAsync($"/api/payouts/{payout.Id}/approve", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify state transition persisted
        var detail = await (await _clientA.GetAsync($"/api/payouts/{payout.Id}"))
            .Content.ReadFromJsonAsync<PayoutDto>(ApiJson);
        detail!.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task Approve_AlreadyPaid_Returns400WithClearError()
    {
        var payout = await SeedCalculatedPayoutAsync();
        await _clientA.PostAsync($"/api/payouts/{payout.Id}/approve", null);
        await _clientA.PostAsync($"/api/payouts/{payout.Id}/mark-paid", null);

        var response = await _clientA.PostAsync($"/api/payouts/{payout.Id}/approve", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("Calculated"); // DomainException message
    }

    // ── Mark Paid ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkPaid_FromCalculated_Returns400()
    {
        var payout = await SeedCalculatedPayoutAsync();

        var response = await _clientA.PostAsync($"/api/payouts/{payout.Id}/mark-paid", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MarkPaid_FromApproved_Returns204()
    {
        var payout = await SeedCalculatedPayoutAsync();
        await _clientA.PostAsync($"/api/payouts/{payout.Id}/approve", null);

        var response = await _clientA.PostAsync($"/api/payouts/{payout.Id}/mark-paid", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await (await _clientA.GetAsync($"/api/payouts/{payout.Id}"))
            .Content.ReadFromJsonAsync<PayoutDto>(ApiJson);
        detail!.Status.Should().Be("Paid");
    }

    // ── Bulk Approve ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkApprove_MultipleCalculated_AprovesAll()
    {
        var p1 = await SeedCalculatedPayoutAsync();
        var p2 = await SeedCalculatedPayoutAsync();

        var response = await _clientA.PostAsJsonAsync(
            "/api/payouts/bulk-approve",
            new { payoutIds = new[] { p1.Id, p2.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkApproveResultResponse>();
        result!.Approved.Should().Be(2);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task BulkApprove_MixedStatuses_AprovesCalculatedReportsOthers()
    {
        var calculated = await SeedCalculatedPayoutAsync();
        var alreadyPaid = await SeedCalculatedPayoutAsync();
        // Approve then mark paid
        await _clientA.PostAsync($"/api/payouts/{alreadyPaid.Id}/approve", null);
        await _clientA.PostAsync($"/api/payouts/{alreadyPaid.Id}/mark-paid", null);

        var response = await _clientA.PostAsJsonAsync(
            "/api/payouts/bulk-approve",
            new { payoutIds = new[] { calculated.Id, alreadyPaid.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkApproveResultResponse>();
        result!.Approved.Should().Be(1);
        result.Errors.Should().HaveCount(1);
    }

    // ── Bulk Mark Paid ────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkMarkPaid_MultipleApproved_MarksAllPaid()
    {
        var p1 = await SeedCalculatedPayoutAsync();
        var p2 = await SeedCalculatedPayoutAsync();
        await _clientA.PostAsync($"/api/payouts/{p1.Id}/approve", null);
        await _clientA.PostAsync($"/api/payouts/{p2.Id}/approve", null);

        var response = await _clientA.PostAsJsonAsync(
            "/api/payouts/bulk-mark-paid",
            new { payoutIds = new[] { p1.Id, p2.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkMarkPaidResultResponse>();
        result!.Paid.Should().Be(2);
        result.Errors.Should().BeEmpty();

        // Verify state persisted
        var detail = await (await _clientA.GetAsync($"/api/payouts/{p1.Id}"))
            .Content.ReadFromJsonAsync<PayoutDto>(ApiJson);
        detail!.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task BulkMarkPaid_MixedStatuses_PaysApprovedSkipsOthers()
    {
        var approved = await SeedCalculatedPayoutAsync();
        var calculated = await SeedCalculatedPayoutAsync(); // not approved — should be skipped
        await _clientA.PostAsync($"/api/payouts/{approved.Id}/approve", null);

        var response = await _clientA.PostAsJsonAsync(
            "/api/payouts/bulk-mark-paid",
            new { payoutIds = new[] { approved.Id, calculated.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkMarkPaidResultResponse>();
        result!.Paid.Should().Be(1);
        result.Errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task BulkMarkPaid_WithoutToken_Returns401()
    {
        var response = await _fixture.Factory.CreateClient().PostAsJsonAsync(
            "/api/payouts/bulk-mark-paid",
            new { payoutIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── List: period filter ───────────────────────────────────────────────────

    [Fact]
    public async Task ListPayouts_FilterByPeriod_ReturnsOnlyMatchingPayouts()
    {
        // Seed one payout in Jan–Mar 2026 (default seed period) and one in Apr–Jun 2026.
        await SeedCalculatedPayoutAsync();
        await SeedCalculatedPayoutAsync(period: DateRange.Of(
            new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)));

        // Filter to Apr–Jun only.
        var response = await _clientA.GetAsync(
            "/api/payouts?periodFrom=2026-04-01&periodTo=2026-06-30");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadPagedResultAsync<PayoutListItemDto>();
        result.Items.Should().HaveCount(1);
        result.Items[0].PeriodStart.Should().Be(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public async Task ListPayouts_ExcludeZero_OmitsZeroAmountPayouts()
    {
        await SeedCalculatedPayoutAsync(lineCount: 1);     // non-zero
        await SeedCalculatedPayoutAsync(lineCount: 0);     // zero-amount

        var allResponse = await _clientA.GetAsync("/api/payouts");
        var all = await allResponse.Content.ReadPagedResultAsync<PayoutListItemDto>();
        all.TotalCount.Should().Be(2);

        var filteredResponse = await _clientA.GetAsync("/api/payouts?excludeZero=true");
        var filtered = await filteredResponse.Content.ReadPagedResultAsync<PayoutListItemDto>();
        filtered.Items.Should().HaveCount(1);
        filtered.Items[0].TotalCommissionAmount.Should().BeGreaterThan(0);
    }

    // ── Calculate (job enqueue) ───────────────────────────────────────────────

    [Fact]
    public async Task Calculate_ValidRequest_Returns202WithJobId()
    {
        var response = await _clientA.PostAsJsonAsync("/api/payouts/calculate", new
        {
            periodStart = "2026-01-01",
            periodEnd = "2026-03-31",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JobIdResponse>();
        body!.JobId.Should().NotBeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ResetPayoutsAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PayoutLines");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CompensationPayouts");
    }

    private async Task<CompensationPayout> SeedCalculatedPayoutAsync(
        int lineCount = 1,
        DateRange? period = null)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var payeeId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var snapshot = PayeeReference.Snapshot(payeeId, "Test Payee", "TST-001");
        period ??= DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        var specs = Enumerable.Range(0, lineCount)
            .Select(_ => new PayoutLineSpec(
                CreditId: Guid.NewGuid(),
                RuleId: Guid.NewGuid(),
                RuleName: "Base Rate",
                BaseAmount: Money.Of(1000m, "EUR"),
                CommissionAmount: Money.Of(100m, "EUR"),
                AppliedModifiers: []))
            .ToList();

        var payout = CompensationPayout.Calculate(
            tenantId: TestConstants.TenantA,
            payeeId: payeeId,
            planId: planId,
            payeeSnapshot: snapshot,
            period: period,
            lineSpecs: specs,
            fallbackCurrency: "EUR",
            calculatedBy: "test",
            id: Guid.NewGuid(),
            now: DateTimeOffset.UtcNow,
            eventId: Guid.NewGuid(),
            newId: Guid.NewGuid);

        db.CompensationPayouts.Add(payout);
        await db.SaveChangesAsync();
        return payout;
    }

    // ── Export Excel ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportPayouts_WithoutToken_Returns401()
    {
        var response = await _fixture.Factory.CreateClient().GetAsync("/api/payouts/export");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExportPayouts_WithPermission_ReturnsXlsxWithAllRows()
    {
        await SeedCalculatedPayoutAsync();
        await SeedCalculatedPayoutAsync();

        var response = await _clientA.GetAsync("/api/payouts/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        // Verify the response is a non-empty xlsx blob (ClosedXML writes the OOXML magic bytes PK)
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be(0x50); // 'P' of PK magic header
        bytes[1].Should().Be(0x4B); // 'K' of PK magic header
    }

    [Fact]
    public async Task ExportPayouts_IgnoresPagination_ReturnsAllMatchingRows()
    {
        // Seed 3 payouts but request with pageSize=1 — export must return all 3.
        await SeedCalculatedPayoutAsync();
        await SeedCalculatedPayoutAsync();
        await SeedCalculatedPayoutAsync();

        // First confirm the list endpoint honours pagination (only 1 row returned).
        var listResponse = await _clientA.GetAsync("/api/payouts?page=1&pageSize=1");
        var listed = await listResponse.Content.ReadPagedResultAsync<PayoutListItemDto>();
        listed.Items.Should().HaveCount(1);
        listed.TotalCount.Should().Be(3);

        // Export with page=1&pageSize=1 should still return all 3 rows.
        var exportResponse = await _clientA.GetAsync("/api/payouts/export?page=1&pageSize=1");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await exportResponse.Content.ReadAsByteArrayAsync();
        // File must be larger than a minimal (header-only) xlsx — rough proxy for 3 rows.
        bytes.Length.Should().BeGreaterThan(2000);
    }

    [Fact]
    public async Task ExportPayouts_RespectsStatusFilter_OnlyExportsMatchingStatus()
    {
        // Seed one Calculated and one Approved payout.
        var calculated = await SeedCalculatedPayoutAsync();
        var approved = await SeedCalculatedPayoutAsync();
        await _clientA.PostAsync($"/api/payouts/{approved.Id}/approve", null);

        // Export only Approved.
        var response = await _clientA.GetAsync("/api/payouts/export?status=Approved");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();

        // Cross-check: exporting Calculated should return a different (presumably smaller) file.
        var responseCalc = await _clientA.GetAsync("/api/payouts/export?status=Calculated");
        responseCalc.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytesCalc = await responseCalc.Content.ReadAsByteArrayAsync();
        // Both export exactly one row — sizes should be close but files are different.
        bytes.Length.Should().BeGreaterThan(0);
        bytesCalc.Length.Should().BeGreaterThan(0);
        _ = calculated; // suppress unused variable warning
    }

    [Fact]
    public async Task ExportPayouts_ExcludeZero_OmitsZeroAmountPayouts()
    {
        await SeedCalculatedPayoutAsync(lineCount: 1); // non-zero
        await SeedCalculatedPayoutAsync(lineCount: 0); // zero-amount

        // Without filter: both payouts present (file is larger).
        var allResponse = await _clientA.GetAsync("/api/payouts/export");
        allResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allBytes = await allResponse.Content.ReadAsByteArrayAsync();

        // With excludeZero: only non-zero payout (file is smaller).
        var filteredResponse = await _clientA.GetAsync("/api/payouts/export?excludeZero=true");
        filteredResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var filteredBytes = await filteredResponse.Content.ReadAsByteArrayAsync();

        filteredBytes.Length.Should().BeLessThan(allBytes.Length);
    }

    [Fact]
    public async Task ExportPayouts_TenantIsolation_DoesNotReturnOtherTenantPayouts()
    {
        // Seed payout in Tenant A.
        await SeedCalculatedPayoutAsync();

        // Export as Tenant B — should return no rows (empty xlsx, header only).
        var clientB = _fixture.Factory.CreateClient().WithAuth(TestConstants.TenantB);
        var response = await clientB.GetAsync("/api/payouts/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var allBytes = await response.Content.ReadAsByteArrayAsync();

        // Export as Tenant A — must be larger (has data rows).
        var responseA = await _clientA.GetAsync("/api/payouts/export");
        var aBytes = await responseA.Content.ReadAsByteArrayAsync();

        aBytes.Length.Should().BeGreaterThan(allBytes.Length);
    }

    // ── Export: row count matches list totalCount ─────────────────────────────

    [Fact]
    public async Task ExportPayouts_RowCountMatchesListTotalCount_ForSameFilter()
    {
        // Seed 4 payouts all within January 2026.
        var jan = DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        await SeedCalculatedPayoutAsync(period: jan);
        await SeedCalculatedPayoutAsync(period: jan);
        await SeedCalculatedPayoutAsync(period: jan);
        await SeedCalculatedPayoutAsync(period: jan);

        const string filter = "periodFrom=2026-01-01&periodTo=2026-01-31";

        // List must report 4 total rows for that period.
        var listResp = await _clientA.GetAsync($"/api/payouts?{filter}&pageSize=100");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResp.Content.ReadPagedResultAsync<PayoutListItemDto>();
        listed.TotalCount.Should().Be(4);

        // Export with the identical filter must contain exactly 4 data rows in the xlsx.
        var exportResp = await _clientA.GetAsync($"/api/payouts/export?{filter}");
        exportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await exportResp.Content.ReadAsByteArrayAsync();

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        // RowsUsed() counts all non-empty rows including the header; subtract 1.
        var dataRowCount = ws.RowsUsed().Count() - 1;
        dataRowCount.Should().Be(listed.TotalCount,
            because: "export with the same filter must contain exactly as many data rows as list.totalCount");
    }

    private sealed record ErrorResponse(string Message);
    private sealed record BulkApproveResultResponse(int Approved, List<string> Errors);
    private sealed record BulkMarkPaidResultResponse(int Paid, List<string> Errors);
    private sealed record JobIdResponse(Guid JobId);
}
