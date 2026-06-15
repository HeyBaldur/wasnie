#pragma warning disable CS8602 // Possible null reference — test assertions handle this

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Imports;
using Wasnie.IntegrationTests.Infrastructure;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Services.Imports;

public sealed class PayeeImportExecutionServiceTests
{
    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static readonly Guid TenantA = TestConstants.TenantA;
    private static readonly Guid TenantB = TestConstants.TenantB;

    private static ApplicationDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(tenantId), NoOpPublisher.Instance);
    }

    private static PayeeImportExecutionService CreateSut(ApplicationDbContext db, Guid tenantId)
    {
        var logger = Substitute.For<ILogger<PayeeImportExecutionService>>();
        var tierLimitChecker = Substitute.For<ITierLimitChecker>();
        tierLimitChecker.CheckPayeeImportLimitAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PayeeImportLimitCheck(Blocked: false, Current: 0, Limit: int.MaxValue, Tier: "Scale"));
        return new PayeeImportExecutionService(db, new FixedTenantContext(tenantId), logger, new FakeClock(), new FakeGuidGenerator(), tierLimitChecker);
    }

    private static PayeeImportColumnMapping DefaultMapping() => new()
    {
        FullNameColumn = "Name",
        EmployeeCodeColumn = "Code",
        EmailColumn = "Email",
        HireDateColumn = "Date",
    };

    private static Dictionary<string, string> ValidRowData(
        string name = "Alice Smith",
        string code = "EMP001",
        string email = "alice@company.com",
        string date = "2023-01-15")
        => new() { ["Name"] = name, ["Code"] = code, ["Email"] = email, ["Date"] = date };

    private static PayeeRowValidationResult OkResult(int rowNum, Dictionary<string, string> row)
        => new() { RowNumber = rowNum, OriginalData = row, Issues = [] };

    private static PayeeRowValidationResult ErrorResult(int rowNum, Dictionary<string, string> row)
        => new()
        {
            RowNumber = rowNum,
            OriginalData = row,
            Issues = [new ValidationIssue { Field = "X", Message = "err", Severity = IssueSeverity.Error }],
        };

    private static PayeeRowValidationResult WarnResult(int rowNum, Dictionary<string, string> row)
        => new()
        {
            RowNumber = rowNum,
            OriginalData = row,
            Issues = [new ValidationIssue { Field = "X", Message = "warn", Severity = IssueSeverity.Warning }],
        };

    // ──────────────────────────────────────────────────────────
    //  Happy path
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_AllValidRows_CreatesCorrectNumberOfPayees()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);
        var rows = Enumerable.Range(1, 3)
            .Select(i => ValidRowData(name: $"Person {i}", code: $"EMP{i:D3}", email: $"person{i}@co.com"))
            .ToList();
        var vrs = rows.Select((r, idx) => OkResult(idx + 1, r)).ToList();

        await sut.ExecuteAsync(rows, DefaultMapping(), vrs, false, "admin@co.com", "test.csv");

        var saved = await db.Payees.IgnoreQueryFilters()
            .Where(p => p.TenantId == TenantA).ToListAsync();
        saved.Should().HaveCount(3);
    }

    [Fact]
    public async Task Execute_AllValidRows_ReturnsCorrectCreatedCount()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);
        var rows = Enumerable.Range(1, 5)
            .Select(i => ValidRowData(name: $"P{i}", code: $"C{i:D3}", email: $"p{i}@co.com"))
            .ToList();
        var vrs = rows.Select((r, idx) => OkResult(idx + 1, r)).ToList();

        var result = await sut.ExecuteAsync(rows, DefaultMapping(), vrs, false, "admin@co.com", "test.csv");

        result.CreatedCount.Should().Be(5);
        result.SkippedCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_AllValidRows_CreatesImportAuditRecord()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);
        var row = ValidRowData();
        var vrs = new List<PayeeRowValidationResult> { OkResult(1, row) };

        await sut.ExecuteAsync(new List<Dictionary<string, string>> { row }, DefaultMapping(), vrs, false, "admin@co.com", "myfile.csv");

        var audits = await db.ImportAudits.IgnoreQueryFilters().ToListAsync();
        audits.Should().HaveCount(1);
    }

    [Fact]
    public async Task Execute_AuditRecord_HasCorrectTenantAndFileName()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);
        var row = ValidRowData();
        var vrs = new List<PayeeRowValidationResult> { OkResult(1, row) };

        await sut.ExecuteAsync(new List<Dictionary<string, string>> { row }, DefaultMapping(), vrs, false, "admin@co.com", "payroll_import.csv");

        var audit = (await db.ImportAudits.IgnoreQueryFilters().ToListAsync()).Single();
        audit.TenantId.Should().Be(TenantA);
        audit.OriginalFileName.Should().Be("payroll_import.csv");
    }

    // ──────────────────────────────────────────────────────────
    //  Skip behaviour
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_RowsWithErrors_AreSkipped()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);

        var rowValid = ValidRowData(code: "EMP001", email: "valid@co.com");
        var rowError = ValidRowData(name: "", code: "EMP002", email: "error@co.com");

        var rows = new List<Dictionary<string, string>> { rowValid, rowError };
        var vrs = new List<PayeeRowValidationResult>
        {
            OkResult(1, rowValid),
            ErrorResult(2, rowError),
        };

        var result = await sut.ExecuteAsync(rows, DefaultMapping(), vrs, false, "admin@co.com", "test.csv");

        result.CreatedCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        var saved = await db.Payees.IgnoreQueryFilters().Where(p => p.TenantId == TenantA).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].EmployeeCode.Should().Be("EMP001");
    }

    [Fact]
    public async Task Execute_SkipWarnings_True_SkipsWarningRows()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);

        var rowOk = ValidRowData(code: "EMP001", email: "ok@co.com");
        var rowWarn = ValidRowData(code: "EMP002", email: "warn@co.com");

        var rows = new List<Dictionary<string, string>> { rowOk, rowWarn };
        var vrs = new List<PayeeRowValidationResult>
        {
            OkResult(1, rowOk),
            WarnResult(2, rowWarn),
        };

        var result = await sut.ExecuteAsync(rows, DefaultMapping(), vrs, skipRowsWithWarnings: true, "admin@co.com", "test.csv");

        result.CreatedCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_SkipWarnings_False_ImportsWarningRows()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);

        var rowOk = ValidRowData(code: "EMP001", email: "ok@co.com");
        var rowWarn = ValidRowData(code: "EMP002", email: "warn@co.com");

        var rows = new List<Dictionary<string, string>> { rowOk, rowWarn };
        var vrs = new List<PayeeRowValidationResult>
        {
            OkResult(1, rowOk),
            WarnResult(2, rowWarn),
        };

        var result = await sut.ExecuteAsync(rows, DefaultMapping(), vrs, skipRowsWithWarnings: false, "admin@co.com", "test.csv");

        result.CreatedCount.Should().Be(2);
        result.SkippedCount.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────
    //  Manager resolution
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_CrossRowManagers_PayeeLinkedToManagerAfterImport()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);

        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };

        var managerRow = new Dictionary<string, string>
        {
            ["Name"] = "Boss",
            ["Code"] = "MGR001",
            ["Email"] = "boss@co.com",
            ["Date"] = "2018-01-10",
            ["Mgr"] = "",
        };
        var reportRow = new Dictionary<string, string>
        {
            ["Name"] = "Report",
            ["Code"] = "REP001",
            ["Email"] = "rep@co.com",
            ["Date"] = "2020-03-01",
            ["Mgr"] = "MGR001",
        };

        var rows = new List<Dictionary<string, string>> { managerRow, reportRow };
        var vrs = new List<PayeeRowValidationResult>
        {
            OkResult(1, managerRow),
            OkResult(2, reportRow),
        };

        await sut.ExecuteAsync(rows, mapping, vrs, false, "admin@co.com", "test.csv");

        var manager = await db.Payees.IgnoreQueryFilters().SingleAsync(p => p.EmployeeCode == "MGR001");
        var report = await db.Payees.IgnoreQueryFilters().SingleAsync(p => p.EmployeeCode == "REP001");
        report.ManagerId.Should().Be(manager.Id);
    }

    [Fact]
    public async Task Execute_ManagerExistsInDb_LinkedCorrectly()
    {
        await using var db = CreateDb(TenantA);
        // Pre-insert the manager
        var existingMgr = Payee.Create(TenantA, "DB Manager", "DBMGR001", "dbmgr@co.com",
            new DateOnly(2015, 1, 1), "system", Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.Payees.Add(existingMgr);
        await db.SaveChangesAsync();

        var sut = CreateSut(db, TenantA);
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };

        var reportRow = new Dictionary<string, string>
        {
            ["Name"] = "New Report",
            ["Code"] = "NEWREP001",
            ["Email"] = "newrep@co.com",
            ["Date"] = "2022-01-01",
            ["Mgr"] = "DBMGR001",
        };
        var rows = new List<Dictionary<string, string>> { reportRow };
        var vrs = new List<PayeeRowValidationResult> { OkResult(1, reportRow) };

        await sut.ExecuteAsync(rows, mapping, vrs, false, "admin@co.com", "test.csv");

        var saved = await db.Payees.IgnoreQueryFilters()
            .Where(p => p.TenantId == TenantA)
            .ToListAsync();
        var report = saved.Single(p => p.EmployeeCode == "NEWREP001");
        report.ManagerId.Should().Be(existingMgr.Id);
    }

    // ──────────────────────────────────────────────────────────
    //  Partial import
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_SomeErrors_CreatesOnlyValidRows_SkippedCountCorrect()
    {
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);

        var rows = new List<Dictionary<string, string>>();
        var vrs = new List<PayeeRowValidationResult>();

        // 4 valid, 3 error rows
        for (var i = 1; i <= 4; i++)
        {
            var r = ValidRowData(name: $"Valid {i}", code: $"VALID{i:D3}", email: $"valid{i}@co.com");
            rows.Add(r);
            vrs.Add(OkResult(i, r));
        }
        for (var i = 1; i <= 3; i++)
        {
            var r = ValidRowData(name: $"Error {i}", code: $"ERR{i:D3}", email: $"err{i}@co.com");
            rows.Add(r);
            vrs.Add(ErrorResult(rows.Count, r));
        }

        var result = await sut.ExecuteAsync(rows, DefaultMapping(), vrs, false, "admin@co.com", "test.csv");

        result.CreatedCount.Should().Be(4);
        result.SkippedCount.Should().Be(3);
        var saved = await db.Payees.IgnoreQueryFilters().Where(p => p.TenantId == TenantA).ToListAsync();
        saved.Should().HaveCount(4);
    }

    // ──────────────────────────────────────────────────────────
    //  Tenant isolation
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_TenantId_TakenFromContext_NotFromData()
    {
        // The execution service uses ITenantContext.TenantId — not anything in the row data.
        await using var db = CreateDb(TenantA);
        var sut = CreateSut(db, TenantA);

        var row = ValidRowData();
        var vrs = new List<PayeeRowValidationResult> { OkResult(1, row) };

        await sut.ExecuteAsync(new List<Dictionary<string, string>> { row }, DefaultMapping(), vrs, false, "admin@co.com", "test.csv");

        var saved = await db.Payees.IgnoreQueryFilters().ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].TenantId.Should().Be(TenantA);
    }
}
