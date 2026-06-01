#pragma warning disable CS8602 // Possible null reference — test assertions handle this

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Imports;
using Wasnie.IntegrationTests.Infrastructure;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Services.Imports;

public sealed class PayeeImportValidationServiceTests
{
    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    // All existing tests treat Email + HireDate as required (pre-WI-PROD-A.1 behaviour).
    private static IFieldRequirementService AllRequiredFields() => new AlwaysRequiredService();

    private sealed class AlwaysRequiredService : IFieldRequirementService
    {
        public Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class AlwaysRequiredExceptService(string exemptField) : IFieldRequirementService
    {
        public Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken ct = default) =>
            Task.FromResult(fieldName != exemptField);
    }

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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
            new DateOnly(2021, 1, 1), "system", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
            new DateOnly(2021, 1, 1), "system", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
            new DateOnly(2021, 1, 1), "system", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await dbB.SaveChangesAsync();

        // Validate with TenantA context — global filter means TenantB data is invisible
        await using var dbA = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(dbA, new FakeClock(), AllRequiredFields());
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());

        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };
        var manager = new Dictionary<string, string>
        {
            ["Name"] = "The Manager",
            ["Code"] = "MGR001",
            ["Email"] = "mgr@company.com",
            ["Date"] = "2018-01-10",
            ["Mgr"] = "",
        };
        var report = new Dictionary<string, string>
        {
            ["Name"] = "Report One",
            ["Code"] = "REP001",
            ["Email"] = "rep1@company.com",
            ["Date"] = "2020-03-01",
            ["Mgr"] = "MGR001",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { manager, report }, mapping);

        results[1].Issues.Should().NotContain(i => i.Field == "ManagerEmployeeCode" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_ManagerCodeNotInFileOrDb_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());

        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };
        var row = new Dictionary<string, string>
        {
            ["Name"] = "Some Person",
            ["Code"] = "EMP001",
            ["Email"] = "person@company.com",
            ["Date"] = "2022-01-01",
            ["Mgr"] = "NONEXISTENT",
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
            new DateOnly(2018, 1, 1), "system", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };
        var row = new Dictionary<string, string>
        {
            ["Name"] = "New Report",
            ["Code"] = "REP001",
            ["Email"] = "rep@company.com",
            ["Date"] = "2022-01-01",
            ["Mgr"] = "MGR999",
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
        var clock = new FakeClock();
        var sut = new PayeeImportValidationService(db, clock, AllRequiredFields());
        var recentDate = clock.UtcNow.AddDays(-5).ToString("yyyy-MM-dd");
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
        // Role optional → warning; use a service that requires Email/HireDate but not Role
        var sut = new PayeeImportValidationService(db, new FakeClock(),
            new AlwaysRequiredExceptService("Role"));
        var row = ValidRow();
        row["Role"] = "";  // empty role column present
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
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
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow(name: "García López");
        var mapping = DefaultMapping();

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].Issues.Should().NotContain(i => i.Field == "FullName" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  EmploymentType validation
    // ──────────────────────────────────────────────────────────

    private sealed class SelectiveService(string requiredField) : IFieldRequirementService
    {
        public Task<bool> IsRequiredAsync(string entityName, string fieldName, CancellationToken ct = default) =>
            Task.FromResult(fieldName == requiredField);
    }

    [Fact]
    public async Task Validate_ValidEmploymentType_NoError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow();
        row["ContractType"] = "FullTime";
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            EmploymentTypeColumn = "ContractType",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].Issues.Should().NotContain(i => i.Field == "EmploymentType" && i.Severity == IssueSeverity.Error);
    }

    [Theory]
    [InlineData("FullTime")]
    [InlineData("fulltime")]
    [InlineData("CONTRACTOR")]
    [InlineData("Temporary")]
    [InlineData("PartTime")]
    public async Task Validate_AllValidEmploymentTypes_NoError(string value)
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow();
        row["ContractType"] = value;
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            EmploymentTypeColumn = "ContractType",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].Issues.Should().NotContain(i => i.Field == "EmploymentType" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_InvalidEmploymentType_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow();
        row["ContractType"] = "Freelance";
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            EmploymentTypeColumn = "ContractType",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "EmploymentType" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_EmptyEmploymentType_RequiredSetting_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), new SelectiveService("EmploymentType"));
        var row = ValidRow();
        row["ContractType"] = "";
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            EmploymentTypeColumn = "ContractType",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "EmploymentType" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Location validation
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_ValidLocation_NoError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow();
        row["Office"] = "Warsaw";
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            LocationColumn = "Office",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].Issues.Should().NotContain(i => i.Field == "Location" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_EmptyLocation_RequiredSetting_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), new SelectiveService("Location"));
        var row = ValidRow();
        row["Office"] = "";
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            LocationColumn = "Office",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "Location" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task Validate_LocationTooLong_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow();
        row["Office"] = new string('A', 201);
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            LocationColumn = "Office",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "Location" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  Role: catalog-driven required
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_EmptyRole_RequiredSetting_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), new SelectiveService("Role"));
        var row = ValidRow();
        row["Role"] = "";
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            RoleColumn = "Role",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "Role" && i.Severity == IssueSeverity.Error);
    }

    // ──────────────────────────────────────────────────────────
    //  IssueCategory — messages include offending values
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_DuplicateCode_HasReferenceCategory()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(Payee.Create(TenantA, "Existing", "EMP001", null, null,
            "system", Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow(code: "EMP001", email: "new@company.com");
        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "EmployeeCode");
        issue.Category.Should().Be(IssueCategory.Reference);
        issue.Message.Should().Contain("EMP001");
    }

    [Fact]
    public async Task Validate_MissingEmailRequired_HasRequiredCategory()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow(email: "");
        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "Email");
        issue.Category.Should().Be(IssueCategory.Required);
        issue.Message.Should().Contain("Settings");
    }

    [Fact]
    public async Task Validate_BadHireDateFormat_HasFormatCategory()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow(date: "not-a-date");
        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "HireDate");
        issue.Category.Should().Be(IssueCategory.Format);
        issue.Message.Should().Contain("not-a-date");
    }

    [Fact]
    public async Task Validate_FutureHireDate_MessageContainsActualDate()
    {
        await using var db = CreateDb(TenantA);
        var clock = new FakeClock(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var sut = new PayeeImportValidationService(db, clock, AllRequiredFields());
        var row = ValidRow(date: "2026-01-01");
        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "HireDate");
        issue.Category.Should().Be(IssueCategory.Format);
        issue.Message.Should().Contain("2026-01-01");
    }

    [Fact]
    public async Task Validate_DuplicateEmail_MessageContainsEmail_CategoryReference()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row1 = ValidRow(code: "EMP001", email: "same@company.com");
        var row2 = ValidRow(code: "EMP002", email: "same@company.com");
        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row1, row2 }, DefaultMapping());

        var issue = results[1].Issues.First(i => i.Field == "Email");
        issue.Category.Should().Be(IssueCategory.Reference);
        issue.Message.Should().Contain("same@company.com");
    }

    [Fact]
    public async Task Validate_InvalidEmailFormat_MessageContainsAddress_CategoryFormat()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow(email: "not-valid");
        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "Email");
        issue.Category.Should().Be(IssueCategory.Format);
        issue.Message.Should().Contain("not-valid");
    }

    [Fact]
    public async Task Validate_MissingHireDateRequired_HasRequiredCategory()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var row = ValidRow(date: "");
        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "HireDate");
        issue.Category.Should().Be(IssueCategory.Required);
        issue.Message.Should().Contain("Settings");
    }

    [Fact]
    public async Task Validate_ManagerNotFound_MessageContainsCode_CategoryReference()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
        var mapping = new PayeeImportColumnMapping
        {
            FullNameColumn = "Name",
            EmployeeCodeColumn = "Code",
            EmailColumn = "Email",
            HireDateColumn = "Date",
            ManagerEmployeeCodeColumn = "Mgr",
        };
        var row = new Dictionary<string, string>
        {
            ["Name"] = "Alice", ["Code"] = "EMP001", ["Email"] = "alice@co.com",
            ["Date"] = "2022-01-01", ["Mgr"] = "MGR_GHOST",
        };

        var results = await sut.ValidateAsync(new List<Dictionary<string, string>> { row }, mapping);

        var issue = results[0].Issues.First(i => i.Field == "ManagerEmployeeCode");
        issue.Category.Should().Be(IssueCategory.Reference);
        issue.Message.Should().Contain("MGR_GHOST");
    }

    // ──────────────────────────────────────────────────────────
    //  Mixed / happy path
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_10ValidRows_ReturnsZeroErrors()
    {
        await using var db = CreateDb(TenantA);
        var sut = new PayeeImportValidationService(db, new FakeClock(), AllRequiredFields());
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
