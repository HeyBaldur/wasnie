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

/// <summary>
/// The 12 reference payee-import cases, automated.
///
/// The specification lives in `Test Files/files/_CASOS.txt` and its 12 .xlsx files. Those files are
/// used as the SPEC ONLY — the data below is an in-memory equivalent so these tests are
/// self-contained and do not depend on an external path (Forma B). Keep the two in sync by hand:
/// if a .xlsx case changes, update the corresponding builder here.
///
/// Layer: validation + execution services over EF InMemory (no Testcontainers, no Docker), which is
/// where the assertions that matter live — the persisted Payee rows.
/// </summary>
public sealed class PayeeImportCaseMatrixTests
{
    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static readonly Guid TenantA = TestConstants.TenantA;

    /// <summary>
    /// Generalises the per-field fakes in PayeeImportValidationServiceTests: name the fields that are
    /// Required; everything else is Optional. `new FieldRequirements()` = all optional.
    /// </summary>
    private sealed class FieldRequirements(params string[] requiredFields) : IFieldRequirementService
    {
        private readonly HashSet<string> _required = new(requiredFields, StringComparer.OrdinalIgnoreCase);

        public Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken ct = default) =>
            Task.FromResult(_required.Contains(fieldName));
    }

    private static ApplicationDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(tenantId), NoOpPublisher.Instance);
    }

    private static PayeeImportExecutionService CreateExecutor(ApplicationDbContext db, Guid tenantId)
    {
        var logger = Substitute.For<ILogger<PayeeImportExecutionService>>();
        var tierLimitChecker = Substitute.For<ITierLimitChecker>();
        tierLimitChecker.CheckPayeeImportLimitAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PayeeImportLimitCheck(Blocked: false, Current: 0, Limit: int.MaxValue, Tier: "Scale"));
        return new PayeeImportExecutionService(
            db, new FixedTenantContext(tenantId), logger, new FakeClock(), new FakeGuidGenerator(), tierLimitChecker);
    }

    // Column names mirror the reference .xlsx headers. Full name is a two-column composition
    // (FirstName + LastName), exactly as the real files are mapped in the wizard.
    private const string ColCode = "EmployeeCode";
    private const string ColFirst = "FirstName";
    private const string ColLast = "LastName";
    private const string ColEmail = "Email";
    private const string ColHireDate = "HireDate";
    private const string ColRole = "Role";
    private const string ColManager = "ManagerEmployeeCode";
    private const string ColEmploymentType = "EmploymentType";
    private const string ColLocation = "Location";

    private static PayeeImportColumnMapping Mapping(
        bool email = true,
        bool hireDate = true,
        bool role = false,
        bool manager = false,
        bool employmentType = false,
        bool location = false) => new()
        {
            FullNameColumns = [ColFirst, ColLast],
            EmployeeCodeColumn = ColCode,
            EmailColumn = email ? ColEmail : null,
            HireDateColumn = hireDate ? ColHireDate : null,
            RoleColumn = role ? ColRole : null,
            ManagerEmployeeCodeColumn = manager ? ColManager : null,
            EmploymentTypeColumn = employmentType ? ColEmploymentType : null,
            LocationColumn = location ? ColLocation : null,
        };

    private sealed record Person(
        string Code, string First, string Last, string Email, string HireDate, string Role);

    /// <summary>The 10 canonical people shared by the reference files (EPO9001..EPO9010).</summary>
    private static readonly Person[] People =
    [
        new("EPO9001", "Agnieszka", "Jankowska", "a.jankowska@epotest.com", "2022-03-15", "Sales Rep"),
        new("EPO9002", "Piotr", "Nowak", "p.nowak@epotest.com", "2021-07-01", "Sales Rep"),
        new("EPO9003", "María", "García", "m.garcia@epotest.com", "2023-01-10", "Account Exec"),
        new("EPO9004", "Javier", "Fernández", "j.fernandez@epotest.com", "2020-11-20", "Account Exec"),
        new("EPO9005", "Lukas", "Müller", "l.muller@epotest.com", "2022-06-05", "Sales Rep"),
        new("EPO9006", "Anna", "Schmidt", "a.schmidt@epotest.com", "2021-09-14", "Team Lead"),
        new("EPO9007", "Giulia", "Rossi", "g.rossi@epotest.com", "2023-04-01", "Sales Rep"),
        new("EPO9008", "Marco", "Bianchi", "m.bianchi@epotest.com", "2019-02-18", "Account Exec"),
        new("EPO9009", "Camille", "Laurent", "c.laurent@epotest.com", "2022-08-22", "Sales Rep"),
        new("EPO9010", "Louis", "Moreau", "l.moreau@epotest.com", "2020-05-30", "Team Lead"),
    ];

    private static Dictionary<string, string> Row(Person p) => new()
    {
        [ColCode] = p.Code,
        [ColFirst] = p.First,
        [ColLast] = p.Last,
        [ColEmail] = p.Email,
        [ColHireDate] = p.HireDate,
        [ColRole] = p.Role,
    };

    private static List<Dictionary<string, string>> AllRows() => People.Select(Row).ToList();

    /// <summary>Runs the real pipeline: validate, then execute with those validation results.</summary>
    private static async Task<(List<PayeeRowValidationResult> Validation, PayeeImportResult Result)> RunAsync(
        ApplicationDbContext db,
        List<Dictionary<string, string>> rows,
        PayeeImportColumnMapping mapping,
        IFieldRequirementService requirements)
    {
        var validator = new PayeeImportValidationService(db, new FakeClock(), requirements);
        var validation = await validator.ValidateAsync(rows, mapping);

        var executor = CreateExecutor(db, TenantA);
        var result = await executor.ExecuteAsync(
            rows, mapping, validation, skipRowsWithWarnings: false, "admin@epotest.com", "case.xlsx");

        return (validation, result);
    }

    private static Task<List<Payee>> SavedAsync(ApplicationDbContext db) =>
        db.Payees.IgnoreQueryFilters().Where(p => p.TenantId == TenantA).ToListAsync();

    // ──────────────────────────────────────────────────────────
    //  Case 01 — happy path, all fields present
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case01_HappyPathAllFields_ImportsEveryRowWithItsValues()
    {
        await using var db = CreateDb(TenantA);

        var (validation, result) = await RunAsync(
            db, AllRows(), Mapping(role: true), new FieldRequirements("Email", "HireDate"));

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(10);
        result.SkippedCount.Should().Be(0);

        var saved = await SavedAsync(db);
        saved.Should().HaveCount(10);

        var first = saved.Single(p => p.EmployeeCode == "EPO9001");
        first.FullName.Should().Be("Agnieszka Jankowska");
        first.Email.Should().Be("a.jankowska@epotest.com");
        first.HireDate.Should().Be(new DateOnly(2022, 3, 15));

        // Non-ASCII names must survive the round trip.
        saved.Should().Contain(p => p.FullName == "María García");
        saved.Should().Contain(p => p.FullName == "Lukas Müller");
    }

    // ──────────────────────────────────────────────────────────
    //  Case 02 — no HireDate column at all (HireDate = Optional)
    //  This is the anti-corruption assert: NULL, never 0001-01-01.
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case02_NoHireDateColumn_HireDateOptional_PersistsNullNotDefaultDate()
    {
        await using var db = CreateDb(TenantA);

        var (validation, result) = await RunAsync(
            db, AllRows(), Mapping(hireDate: false), new FieldRequirements("Email"));

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(10);

        var saved = await SavedAsync(db);
        saved.Should().HaveCount(10);
        saved.Should().OnlyContain(p => p.HireDate == null);

        // Explicit anti-regression: default(DateOnly) is 0001-01-01 and would be non-null.
        saved.Should().NotContain(p => p.HireDate == new DateOnly(1, 1, 1));
    }

    // ──────────────────────────────────────────────────────────
    //  Case 03 — HireDate column present, some rows blank
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case03_HireDateSomeBlank_HireDateOptional_StoresValuesAndNullsPerRow()
    {
        await using var db = CreateDb(TenantA);

        // Blank the hire date on every second person, as 03_hiredate_some_blank.xlsx does.
        var rows = AllRows();
        for (var i = 1; i < rows.Count; i += 2) rows[i][ColHireDate] = "";

        var (validation, result) = await RunAsync(db, rows, Mapping(), new FieldRequirements("Email"));

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(10);

        var saved = await SavedAsync(db);
        saved.Single(p => p.EmployeeCode == "EPO9001").HireDate.Should().Be(new DateOnly(2022, 3, 15));
        saved.Single(p => p.EmployeeCode == "EPO9002").HireDate.Should().BeNull();
        saved.Single(p => p.EmployeeCode == "EPO9003").HireDate.Should().Be(new DateOnly(2023, 1, 10));
        saved.Single(p => p.EmployeeCode == "EPO9004").HireDate.Should().BeNull();

        saved.Count(p => p.HireDate == null).Should().Be(5);
        saved.Should().NotContain(p => p.HireDate == new DateOnly(1, 1, 1));
    }

    // ──────────────────────────────────────────────────────────
    //  Case 04 — no Email column; both settings exercised
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case04_NoEmailColumn_EmailOptional_ImportsWithoutEmail()
    {
        await using var db = CreateDb(TenantA);

        var (validation, result) = await RunAsync(
            db, AllRows(), Mapping(email: false), new FieldRequirements("HireDate"));

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(10);

        var saved = await SavedAsync(db);
        saved.Should().HaveCount(10);
        saved.Should().OnlyContain(p => p.Email == null || p.Email == "");
    }

    [Fact]
    public async Task Case04_NoEmailColumn_EmailRequired_RejectsEveryRow()
    {
        await using var db = CreateDb(TenantA);

        var (validation, result) = await RunAsync(
            db, AllRows(), Mapping(email: false), new FieldRequirements("Email", "HireDate"));

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeTrue());
        validation.Should().AllSatisfy(r => r.Issues.Should().Contain(i =>
            i.Field == "Email" && i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Required));

        result.CreatedCount.Should().Be(0);
        result.SkippedCount.Should().Be(10);
        (await SavedAsync(db)).Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────
    //  Case 05 — Email column present, some rows blank
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case05_EmailSomeBlank_EmailOptional_StoresValuesAndBlanksPerRow()
    {
        await using var db = CreateDb(TenantA);

        // 05_email_some_blank.xlsx blanks the email on EPO9001, 9004, 9007, 9010.
        var blanks = new[] { "EPO9001", "EPO9004", "EPO9007", "EPO9010" };
        var rows = AllRows();
        foreach (var r in rows.Where(r => blanks.Contains(r[ColCode]))) r[ColEmail] = "";

        var (validation, result) = await RunAsync(db, rows, Mapping(), new FieldRequirements("HireDate"));

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(10);

        var saved = await SavedAsync(db);
        saved.Single(p => p.EmployeeCode == "EPO9002").Email.Should().Be("p.nowak@epotest.com");
        saved.Single(p => p.EmployeeCode == "EPO9001").Email.Should().BeNullOrEmpty();
        saved.Count(p => string.IsNullOrEmpty(p.Email)).Should().Be(4);
    }

    // ──────────────────────────────────────────────────────────
    //  Case 06 — one row missing EmployeeCode (always mandatory)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case06_MissingEmployeeCodeOnOneRow_RejectsThatRowAndImportsTheRest()
    {
        await using var db = CreateDb(TenantA);

        var rows = AllRows();
        rows[3][ColCode] = ""; // Javier Fernández, as in the reference file

        // Everything else Optional — EmployeeCode must be required regardless of settings.
        var (validation, result) = await RunAsync(db, rows, Mapping(), new FieldRequirements());

        validation[3].HasErrors.Should().BeTrue();
        validation[3].Issues.Should().Contain(i =>
            i.Field == "EmployeeCode" && i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Required);
        validation.Where((_, i) => i != 3).Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());

        result.CreatedCount.Should().Be(9);
        result.SkippedCount.Should().Be(1);

        var saved = await SavedAsync(db);
        saved.Should().HaveCount(9);
        saved.Should().NotContain(p => p.FullName == "Javier Fernández");
    }

    // ──────────────────────────────────────────────────────────
    //  Case 07 — one row missing the name (always mandatory)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case07_MissingFullNameOnOneRow_RejectsThatRowAndImportsTheRest()
    {
        await using var db = CreateDb(TenantA);

        var rows = AllRows();
        rows[5][ColFirst] = "";  // EPO9006 Anna Schmidt — both name columns blank
        rows[5][ColLast] = "";

        var (validation, result) = await RunAsync(db, rows, Mapping(), new FieldRequirements());

        validation[5].HasErrors.Should().BeTrue();
        validation[5].Issues.Should().Contain(i =>
            i.Field == "FullName" && i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Required);

        result.CreatedCount.Should().Be(9);
        result.SkippedCount.Should().Be(1);
        (await SavedAsync(db)).Should().NotContain(p => p.EmployeeCode == "EPO9006");
    }

    // ──────────────────────────────────────────────────────────
    //  Case 08 — duplicate EmployeeCode within the file
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case08_DuplicateEmployeeCodeWithinFile_ReportsTheDuplicate()
    {
        await using var db = CreateDb(TenantA);

        var rows = AllRows();
        rows[7][ColCode] = "EPO9003"; // Marco Bianchi reuses María García's code

        var (validation, result) = await RunAsync(db, rows, Mapping(), new FieldRequirements());

        // The first occurrence is accepted; the later one is the duplicate.
        validation[2].HasErrors.Should().BeFalse();
        validation[7].HasErrors.Should().BeTrue();
        validation[7].Issues.Should().Contain(i =>
            i.Field == "EmployeeCode" && i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Reference);

        result.CreatedCount.Should().Be(9);
        result.SkippedCount.Should().Be(1);
        (await SavedAsync(db)).Count(p => p.EmployeeCode == "EPO9003").Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────
    //  Case 09 — duplicate Email within the file
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case09_DuplicateEmailWithinFile_ReportsTheDuplicate()
    {
        await using var db = CreateDb(TenantA);

        var rows = AllRows();
        rows[8][ColEmail] = "a.jankowska@epotest.com"; // Camille reuses Agnieszka's email

        var (validation, result) = await RunAsync(db, rows, Mapping(), new FieldRequirements("Email"));

        validation[0].HasErrors.Should().BeFalse();
        validation[8].HasErrors.Should().BeTrue();
        validation[8].Issues.Should().Contain(i =>
            i.Field == "Email" && i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Reference);

        result.CreatedCount.Should().Be(9);
        result.SkippedCount.Should().Be(1);
        (await SavedAsync(db)).Count(p => p.Email == "a.jankowska@epotest.com").Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────
    //  Case 10 — every optional field mapped, manager resolved by code
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case10_AllOptionalFieldsMapped_StoresThemAndResolvesManagerByCode()
    {
        await using var db = CreateDb(TenantA);

        // Enum names, not the hyphenated UI labels — see Case13 for why.
        var employmentTypes = new Dictionary<string, string>
        {
            ["EPO9004"] = "PartTime",
            ["EPO9008"] = "Contractor",
        };
        // EPO9003/9004 -> EPO9006 and EPO9007/9008 -> EPO9010 are forward references in the file;
        // the executor creates every payee first, then assigns managers in a second pass.
        var managers = new Dictionary<string, string>
        {
            ["EPO9003"] = "EPO9006",
            ["EPO9004"] = "EPO9006",
            ["EPO9007"] = "EPO9010",
            ["EPO9008"] = "EPO9010",
            ["EPO9010"] = "EPO9006",
        };
        var locations = new Dictionary<string, string>
        {
            ["EPO9001"] = "Warsaw", ["EPO9002"] = "Krakow", ["EPO9003"] = "Madrid",
            ["EPO9004"] = "Barcelona", ["EPO9005"] = "Berlin", ["EPO9006"] = "Munich",
            ["EPO9007"] = "Milan", ["EPO9008"] = "Rome", ["EPO9009"] = "Paris", ["EPO9010"] = "Lyon",
        };

        var rows = AllRows();
        foreach (var r in rows)
        {
            var code = r[ColCode];
            r[ColManager] = managers.GetValueOrDefault(code, "");
            r[ColEmploymentType] = employmentTypes.GetValueOrDefault(code, "FullTime");
            r[ColLocation] = locations[code];
        }

        var mapping = Mapping(role: true, manager: true, employmentType: true, location: true);
        var (validation, result) = await RunAsync(db, rows, mapping, new FieldRequirements("Email", "HireDate"));

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(10);

        var saved = await SavedAsync(db);
        var lead = saved.Single(p => p.EmployeeCode == "EPO9006");
        var report = saved.Single(p => p.EmployeeCode == "EPO9003");

        // WITH a manager: the manager-resolution pass must assign ManagerId WITHOUT erasing
        // anything else. Regression guard for the Payee.Update full-replace data loss.
        report.Role.Should().Be("Account Exec");
        report.Location.Should().Be("Madrid");
        report.EmploymentType.Should().Be(EmploymentType.FullTime);
        report.HireDate.Should().Be(new DateOnly(2023, 1, 10));
        report.Email.Should().Be("m.garcia@epotest.com");
        report.ManagerId.Should().Be(lead.Id);

        saved.Single(p => p.EmployeeCode == "EPO9004").EmploymentType.Should().Be(EmploymentType.PartTime);
        saved.Single(p => p.EmployeeCode == "EPO9008").EmploymentType.Should().Be(EmploymentType.Contractor);

        // WITHOUT a manager: phase 2 skips these rows entirely — assert they kept their values too,
        // so the test would still catch a regression that only spared the skipped rows.
        var noManager = saved.Single(p => p.EmployeeCode == "EPO9001");
        noManager.ManagerId.Should().BeNull();
        noManager.Location.Should().Be("Warsaw");
        noManager.EmploymentType.Should().Be(EmploymentType.FullTime);

        // Every row carried a location and an employment type; none may be lost.
        saved.Should().OnlyContain(p => p.Location != null);
        saved.Should().OnlyContain(p => p.EmploymentType != null);
    }

    // ──────────────────────────────────────────────────────────
    //  Case 11 — email and hire date blank on the same rows
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case11_MixedBlanks_BothOptional_EachMissingValueIsNull()
    {
        await using var db = CreateDb(TenantA);

        // 11_mixed_blanks_optionals.xlsx has only 6 rows, with a different blank per row.
        var rows = AllRows().Take(6).ToList();
        rows[0][ColEmail] = ""; rows[0][ColHireDate] = "";   // both blank
        rows[2][ColEmail] = "";                               // email only
        rows[3][ColHireDate] = "";                            // hire date only
        rows[4][ColEmail] = "";                               // email only

        var (validation, result) = await RunAsync(db, rows, Mapping(), new FieldRequirements());

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(6);

        var saved = await SavedAsync(db);
        var both = saved.Single(p => p.EmployeeCode == "EPO9001");
        both.Email.Should().BeNullOrEmpty();
        both.HireDate.Should().BeNull();

        saved.Single(p => p.EmployeeCode == "EPO9002").Email.Should().Be("p.nowak@epotest.com");
        saved.Single(p => p.EmployeeCode == "EPO9002").HireDate.Should().Be(new DateOnly(2021, 7, 1));
        saved.Single(p => p.EmployeeCode == "EPO9003").HireDate.Should().Be(new DateOnly(2023, 1, 10));
        saved.Single(p => p.EmployeeCode == "EPO9004").HireDate.Should().BeNull();

        saved.Should().NotContain(p => p.HireDate == new DateOnly(1, 1, 1));
    }

    // ──────────────────────────────────────────────────────────
    //  Case 12 — only the two always-mandatory fields
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case12_OnlyMandatoryFields_EverythingElseOptional_Imports()
    {
        await using var db = CreateDb(TenantA);

        var rows = People.Select(p => new Dictionary<string, string>
        {
            [ColCode] = p.Code,
            [ColFirst] = p.First,
            [ColLast] = p.Last,
        }).ToList();

        var mapping = Mapping(email: false, hireDate: false);
        var (validation, result) = await RunAsync(db, rows, mapping, new FieldRequirements());

        validation.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
        result.CreatedCount.Should().Be(10);

        var saved = await SavedAsync(db);
        saved.Should().HaveCount(10);
        saved.Should().OnlyContain(p => p.HireDate == null);
        saved.Should().OnlyContain(p => p.Role == null);
        saved.Single(p => p.EmployeeCode == "EPO9010").FullName.Should().Be("Louis Moreau");
    }

    // ──────────────────────────────────────────────────────────
    //  Case 13 — pins the UI-label vs importer seam (see report)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Case13_HyphenatedEmploymentTypeLabel_IsRejected_DocumentingTheUiSeam()
    {
        await using var db = CreateDb(TenantA);

        // "Full-time" is exactly what the UI renders (PAYEES.EMPLOYMENT_TYPE_FULLTIME), but the
        // domain enum is `FullTime`, so Enum.TryParse rejects the hyphenated form. This test pins
        // today's behaviour; if the importer ever learns to normalise the label, it should fail
        // and be updated deliberately rather than the seam being lost silently.
        var rows = AllRows().Take(1).ToList();
        rows[0][ColEmploymentType] = "Full-time";

        var mapping = Mapping(employmentType: true);
        var (validation, result) = await RunAsync(db, rows, mapping, new FieldRequirements());

        validation[0].HasErrors.Should().BeTrue();
        validation[0].Issues.Should().Contain(i =>
            i.Field == "EmploymentType" && i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Format);

        result.CreatedCount.Should().Be(0);
        (await SavedAsync(db)).Should().BeEmpty();
    }
}
