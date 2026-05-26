#pragma warning disable CS8602 // Possible null reference — test assertions handle this

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Models.Imports;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Imports;
using Wasnie.IntegrationTests.Infrastructure;

namespace Wasnie.IntegrationTests.Services.Imports;

public sealed class PayeeImportValidationServiceTests
{
    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(tenantId), NoOpPublisher.Instance);
    }

    private static readonly Guid TenantA = TestConstants.TenantA;
    private static readonly Guid TenantB = TestConstants.TenantB;

    private static PayeeImportColumnMapping DefaultMapping() => new()
    {
        FullNameColumn = "Name",
        EmployeeCodeColumn = "Code",
        EmailColumn = "Email",
        HireDateColumn = "Date",
    };

    private static Dictionary<string, string> ValidRow(
        string name = "Alice Smith",
        string code = "EMP001",
        string email = "alice@company.com",
        string date = "2023-01-15")
        => new() { ["Name"] = name, ["Code"] = code, ["Email"] = email, ["Date"] = date };

    // ──────────────────────────────────────────────────────────
    //  Required field tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_MissingFullName_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(name: "");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "FullName" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_MissingEmployeeCode_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(code: "");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "EmployeeCode" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_MissingEmail_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(email: "");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "Email" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_MissingHireDate_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(date: "");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "HireDate" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Email format
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("notanemail")]
    [InlineData("missing@domain")]
    [InlineData("@nolocalpart.com")]
    [InlineData("has space@test.com")]
    public async Task Validate_InvalidEmailFormats_ReturnError(string badEmail)
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(email: badEmail);
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "Email" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Date range
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_FutureHireDate_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var futureDate = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd");
        var row = ValidRow(date: futureDate);
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "HireDate" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_HireDateBefore1950_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(date: "1949-12-31");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "HireDate" && i.Severity == IssueSeverity.Error);
    }

    [Theory]
    [InlineData("2023-01-15")]
    [InlineData("01/15/2023")]
    [InlineData("15/01/2023")]
    public async Task Validate_ValidDateFormats_NoDateError(string dateStr)
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(date: dateStr);
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].Issues.Should().NotContain(i => i.Field == "HireDate" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Duplicates
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_DuplicateCodeWithinFile_SecondRowGetsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row1 = ValidRow(code: "EMP001", email: "alice@company.com");
        var row2 = ValidRow(code: "EMP001", email: "bob@company.com");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row1, row2 }, mapping);

        results[0].HasErrors.Should().BeFalse();
        results[1].HasErrors.Should().BeTrue();
        results[1].Issues.Should().Contain(i => i.Field == "EmployeeCode" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_DuplicateEmailWithinFile_SecondRowGetsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row1 = ValidRow(code: "EMP001", email: "same@company.com");
        var row2 = ValidRow(code: "EMP002", email: "same@company.com");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row1, row2 }, mapping);

        results[0].HasErrors.Should().BeFalse();
        results[1].HasErrors.Should().BeTrue();
        results[1].Issues.Should().Contain(i => i.Field == "Email" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_CodeAlreadyInDb_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        // Pre-insert a payee with EMP001
        db.Payees.Add(Payee.Create(TenantA, "Existing Person", "EMP001", "existing@company.com",
            new DateOnly(2021, 1, 1), "system"));
        await db.SaveChangesAsync();

        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(code: "EMP001", email: "newemail@company.com");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "EmployeeCode" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_EmailAlreadyInDb_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(Payee.Create(TenantA, "Existing Person", "EMP999", "alice@company.com",
            new DateOnly(2021, 1, 1), "system"));
        await db.SaveChangesAsync();

        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(code: "EMP001", email: "alice@company.com");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "Email" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_DuplicateInOtherTenant_NotAnError()
    {
        // Insert EMP001 in TenantB
        await using var dbB = CreateDb(TenantB);
        dbB.Payees.Add(Payee.Create(TenantB, "TenantB Person", "EMP001", "alice@company.com",
            new DateOnly(2021, 1, 1), "system"));
        await dbB.SaveChangesAsync();

        // Validate with TenantA context — global filter means TenantB data is invisible
        await using var dbA = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(dbA);
        var row = ValidRow(code: "EMP001", email: "alice@company.com");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        // EMP001 / alice@company.com should NOT appear as duplicates for TenantA
        results[0].Issues.Should().NotContain(i =>
            (i.Field == "EmployeeCode" || i.Field == "Email") && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Manager references
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_CrossRowManagerReference_NoError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);

        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name", EmployeeCodeColumn = "Code",
            EmailColumn = "Email", HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };
        var manager = new Dictionary<string, string>
        {
            ["Name"] = "The Manager", ["Code"] = "MGR001",
            ["Email"] = "mgr@company.com", ["Date"] = "2018-01-10", ["Mgr"] = "",
        };
        var report = new Dictionary<string, string>
        {
            ["Name"] = "Report One", ["Code"] = "REP001",
            ["Email"] = "rep1@company.com", ["Date"] = "2020-03-01", ["Mgr"] = "MGR001",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { manager, report }, mapping);

        results[1].Issues.Should().NotContain(i => i.Field == "ManagerEmployeeCode" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_ManagerCodeNotInFileOrDb_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);

        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name", EmployeeCodeColumn = "Code",
            EmailColumn = "Email", HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };
        var row = new Dictionary<string, string>
        {
            ["Name"] = "Some Person", ["Code"] = "EMP001",
            ["Email"] = "person@company.com", ["Date"] = "2022-01-01", ["Mgr"] = "NONEXISTENT",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "ManagerEmployeeCode" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_ManagerCodeExistsInDb_NoError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(Payee.Create(TenantA, "DB Manager", "MGR999", "mgr999@company.com",
            new DateOnly(2018, 1, 1), "system"));
        await db.SaveChangesAsync();

        var sut = new PayeeImportValidationService(db);
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name", EmployeeCodeColumn = "Code",
            EmailColumn = "Email", HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };
        var row = new Dictionary<string, string>
        {
            ["Name"] = "New Report", ["Code"] = "REP001",
            ["Email"] = "rep@company.com", ["Date"] = "2022-01-01", ["Mgr"] = "MGR999",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].Issues.Should().NotContain(i => i.Field == "ManagerEmployeeCode" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Warnings
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_PersonalEmailDomain_ReturnsWarning()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(email: "test@gmail.com");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeFalse();
        results[0].HasWarnings.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "Email" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public async Task Validate_HireDateWithinLast30Days_ReturnsWarning()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var recentDate = DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-dd");
        var row = ValidRow(date: recentDate);
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeFalse();
        results[0].HasWarnings.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "HireDate" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public async Task Validate_EmptyRole_ReturnsWarning()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow();
        row["Role"] = "";  // empty role column present
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name", EmployeeCodeColumn = "Code",
            EmailColumn = "Email", HireDateColumn = "Date",
            RoleColumn = "Role",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeFalse();
        results[0].Issues.Should().Contain(i => i.Field == "Role" && i.Severity == IssueSeverity.Warning);
    }

    // ──────────────────────────────────────────────────────────
    //  Special characters
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_NameWithAccents_NoError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var row = ValidRow(name: "García López");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].Issues.Should().NotContain(i => i.Field == "FullName" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Mixed / happy path
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_10ValidRows_ReturnsZeroErrors()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db);
        var mapping = DefaultMapping();

        var rows = Enumerable.Range(1, 10)
            .Select(i => ValidRow(
                name: $"Person {i:D2}",
                code: $"EMP{i:D3}",
                email: $"person{i:D2}@company.com",
                date: $"202{i % 4}-01-15"))
            .ToList();

        var results = await sut.ValidateAsync(rows, mapping);

        results.Should().HaveCount(10);
        results.Should().AllSatisfy(r => r.HasErrors.Should().BeFalse());
    }
}
