#pragma warning disable CS8602 // Possible null reference — test assertions handle this

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Models.Imports;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Imports;
using Wasnie.IntegrationTests.Infrastructure;
using Wasnie.IntegrationTests.TestDoubles;

namespace Wasnie.IntegrationTests.Services.Imports;

/// <summary>
/// Unit-style tests for TransactionImportValidationService using in-memory EF Core.
/// Tests each validation rule in isolation.
/// </summary>
public sealed class TransactionImportValidationServiceTests
{
    private static readonly Guid TenantA = TestConstants.TenantA;
    private static readonly DateOnly HireDate = new(2022, 1, 1);
    private static readonly DateTimeOffset Now = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(tenantId), NoOpPublisher.Instance);
    }

    private static FakeClock ClockAt(DateTimeOffset now) => new(now.UtcDateTime);

    private static TransactionImportColumnMapping DefaultMapping() => new()
    {
        ReferenceNumberColumn = "Ref",
        PayeeCodeColumn = "Code",
        AmountColumn = "Amount",
        CurrencyColumn = "Currency",
        TransactionDateColumn = "Date",
        ExternalIdColumn = "ExtId",
    };

    private static Dictionary<string, string> ValidRow(
        string refNum = "TXN-001",
        string code = "EMP001",
        string amount = "1000.00",
        string currency = "USD",
        string date = "2024-01-15",
        string extId = "") =>
        new()
        {
            ["Ref"] = refNum,
            ["Code"] = code,
            ["Amount"] = amount,
            ["Currency"] = currency,
            ["Date"] = date,
            ["ExtId"] = extId,
        };

    private static Payee MakePayee(Guid tenantId, string code) =>
        Payee.Create(tenantId, $"Test {code}", code, $"{code}@test.com",
            HireDate, "system", Guid.NewGuid(), DateTimeOffset.UtcNow);

    // ──────────────────────────────────────────────────────────────────────────
    //  ReferenceNumber
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_EmptyReferenceNumber_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync(
            [ValidRow(refNum: "")],
            DefaultMapping());

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "referenceNumber");
    }

    [Fact]
    public async Task Validate_DuplicateReferenceInFile_SecondRowHasError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var rows = new List<Dictionary<string, string>>
        {
            ValidRow(refNum: "TXN-DUP", code: "EMP001"),
            ValidRow(refNum: "TXN-DUP", code: "EMP001"),
        };

        var results = await sut.ValidateAsync(rows, DefaultMapping());

        results[0].HasErrors.Should().BeFalse();
        results[1].HasErrors.Should().BeTrue();
        results[1].Issues.Should().Contain(i => i.Field == "referenceNumber");
    }

    [Fact]
    public async Task Validate_ReferenceNumberAlreadyInDb_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var payee = MakePayee(TenantA, "EMP001");
        db.Payees.Add(payee);
        db.CompensationTransactions.Add(CompensationTransaction.Ingest(
            TenantA, "TXN-EXISTS", payee.Id, Money.Of(100m, "USD"),
            new DateOnly(2024, 1, 1), TransactionSource.Manual,
            "system", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync(
            [ValidRow(refNum: "TXN-EXISTS")],
            DefaultMapping());

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "referenceNumber");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  PayeeCode
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_EmptyPayeeCode_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(code: "")], DefaultMapping());

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "payeeCode");
    }

    [Fact]
    public async Task Validate_UnknownPayeeCode_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(code: "NONEXISTENT")], DefaultMapping());

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "payeeCode");
    }

    [Fact]
    public async Task Validate_KnownPayeeCode_NoError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(code: "EMP001")], DefaultMapping());

        results[0].Issues.Should().NotContain(i => i.Field == "payeeCode");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Amount
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("abc")]
    public async Task Validate_UnparseableAmount_ReturnsError(string amount)
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(amount: amount)], DefaultMapping());

        results[0].Issues.Should().Contain(i => i.Field == "amount");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5.00")]
    [InlineData("0.00")]
    public async Task Validate_ZeroOrNegativeAmount_ReturnsError(string amount)
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(amount: amount)], DefaultMapping());

        results[0].Issues.Should().Contain(i => i.Field == "amount");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Currency
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("usd")]
    [InlineData("123")]
    [InlineData("")]
    public async Task Validate_InvalidCurrency_ReturnsError(string currency)
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(currency: currency)], DefaultMapping());

        results[0].Issues.Should().Contain(i => i.Field == "currency");
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("PLN")]
    public async Task Validate_ValidCurrency_NoError(string currency)
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(currency: currency)], DefaultMapping());

        results[0].Issues.Should().NotContain(i => i.Field == "currency");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  TransactionDate
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_UnparseableDate_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(date: "not-a-date")], DefaultMapping());

        results[0].Issues.Should().Contain(i => i.Field == "transactionDate");
    }

    [Fact]
    public async Task Validate_DateBefore2000_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(date: "1999-12-31")], DefaultMapping());

        results[0].Issues.Should().Contain(i => i.Field == "transactionDate");
    }

    [Fact]
    public async Task Validate_FutureDate_ReturnsError()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var clock = ClockAt(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = new TransactionImportValidationService(db, clock);
        var results = await sut.ValidateAsync([ValidRow(date: "2025-12-31")], DefaultMapping());

        results[0].Issues.Should().Contain(i => i.Field == "transactionDate");
    }

    [Theory]
    [InlineData("2024-04-01")]   // ISO 8601 — primary CSV back-compat
    [InlineData("04/01/2024")]   // MM/dd/yyyy  → April 1, 2024
    [InlineData("01/04/2024")]   // dd/MM/yyyy  → April 1, 2024
    public async Task Validate_ValidDateFormats_NoDateError(string dateStr)
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(date: dateStr)], DefaultMapping());

        results[0].Issues.Should().NotContain(i => i.Field == "transactionDate",
            $"'{dateStr}' is a valid date format");
    }

    [Fact]
    public async Task Validate_GarbageDate_ErrorMessageContainsActualValue()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(date: "hello")], DefaultMapping());

        results[0].HasErrors.Should().BeTrue();
        var issue = results[0].Issues.First(i => i.Field == "transactionDate");
        issue.Message.Should().Contain("hello",
            "error message must include the bad value so the user knows which cell to fix");
    }

    [Fact]
    public async Task Validate_DateParsing_CultureIndependent()
    {
        // With pl-PL culture (comma decimal, dd/MM/yyyy date), ISO date "2024-01-15"
        // must still parse correctly — no thread-culture dependency.
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture =
            System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            var sut = new TransactionImportValidationService(db, ClockAt(Now));
            var results = await sut.ValidateAsync([ValidRow(date: "2024-01-15")], DefaultMapping());

            results[0].Issues.Should().NotContain(i => i.Field == "transactionDate",
                "ISO date must parse correctly regardless of thread culture");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task Validate_DateExactlyAtMinBoundary_Passes()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(date: "2000-01-01")], DefaultMapping());

        results[0].Issues.Should().NotContain(i => i.Field == "transactionDate");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  ExternalId
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_ExternalIdAlreadyInDb_ReturnsWarning()
    {
        await using var db = CreateDb(TenantA);
        var payee = MakePayee(TenantA, "EMP001");
        db.Payees.Add(payee);
        db.CompensationTransactions.Add(CompensationTransaction.Ingest(
            TenantA, "TXN-OLD", payee.Id, Money.Of(100m, "USD"),
            new DateOnly(2024, 1, 1), TransactionSource.EtlImport,
            "system", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(),
            externalId: "EXT-001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync(
            [ValidRow(refNum: "TXN-NEW", extId: "EXT-001")],
            DefaultMapping());

        results[0].HasErrors.Should().BeFalse();
        results[0].HasWarnings.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "externalId" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public async Task Validate_DuplicateExternalIdWithinFile_SecondRowIsWarning()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var rows = new List<Dictionary<string, string>>
        {
            ValidRow(refNum: "TXN-001", extId: "EXT-DUP"),
            ValidRow(refNum: "TXN-002", extId: "EXT-DUP"),
        };

        var results = await sut.ValidateAsync(rows, DefaultMapping());

        results[0].HasWarnings.Should().BeFalse();
        results[1].HasWarnings.Should().BeTrue();
        results[1].Issues.Should().Contain(i => i.Field == "externalId" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public async Task Validate_NoExternalIdColumn_DoesNotCheckExternalId()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var mappingWithoutExtId = new TransactionImportColumnMapping
        {
            ReferenceNumberColumn = "Ref",
            PayeeCodeColumn = "Code",
            AmountColumn = "Amount",
            CurrencyColumn = "Currency",
            TransactionDateColumn = "Date",
        };

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow()], mappingWithoutExtId);

        results[0].HasErrors.Should().BeFalse();
        results[0].HasWarnings.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Cross-tenant: payee codes from another tenant are not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_PayeeCodeBelongsToOtherTenant_ReturnsError()
    {
        // Seed a payee under TenantB but validate as TenantA — should not be found.
        await using var dbB = CreateDb(TestConstants.TenantB);
        dbB.Payees.Add(MakePayee(TestConstants.TenantB, "EMP-TENB"));
        await dbB.SaveChangesAsync();

        // Validation runs against TenantA's db context — EMP-TENB not visible.
        await using var dbA = CreateDb(TenantA);
        var sut = new TransactionImportValidationService(dbA, ClockAt(Now));
        var results = await sut.ValidateAsync(
            [ValidRow(code: "EMP-TENB")],
            DefaultMapping());

        results[0].HasErrors.Should().BeTrue();
        results[0].Issues.Should().Contain(i => i.Field == "payeeCode");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  IssueCategory — messages include offending values
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_PayeeCodeNotFound_MessageContainsCode_CategoryReference()
    {
        await using var db = CreateDb(TenantA);
        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(code: "GHOST-99")], DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "payeeCode");
        issue.Category.Should().Be(IssueCategory.Reference);
        issue.Message.Should().Contain("GHOST-99");
        issue.Message.Should().Contain("Create the payee first");
    }

    [Fact]
    public async Task Validate_DuplicateReferenceInFile_MessageContainsRef_CategoryReference()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var rows = new List<Dictionary<string, string>>
        {
            ValidRow(refNum: "TXN-DUP"),
            ValidRow(refNum: "TXN-DUP"),
        };

        var results = await sut.ValidateAsync(rows, DefaultMapping());

        var issue = results[1].Issues.First(i => i.Field == "referenceNumber");
        issue.Category.Should().Be(IssueCategory.Reference);
        issue.Message.Should().Contain("TXN-DUP");
    }

    [Fact]
    public async Task Validate_DuplicateReferenceInDb_MessageContainsRef_CategoryReference()
    {
        await using var db = CreateDb(TenantA);
        var payee = MakePayee(TenantA, "EMP001");
        db.Payees.Add(payee);
        db.CompensationTransactions.Add(CompensationTransaction.Ingest(
            TenantA, "TXN-EXISTS", payee.Id, Money.Of(50m, "USD"),
            new DateOnly(2024, 3, 1), TransactionSource.EtlImport,
            "system", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(refNum: "TXN-EXISTS")], DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "referenceNumber");
        issue.Category.Should().Be(IssueCategory.Reference);
        issue.Message.Should().Contain("TXN-EXISTS");
    }

    [Fact]
    public async Task Validate_BadAmount_MessageContainsValue_CategoryFormat()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(amount: "not-a-number")], DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "amount");
        issue.Category.Should().Be(IssueCategory.Format);
        issue.Message.Should().Contain("not-a-number");
    }

    [Fact]
    public async Task Validate_BadCurrency_MessageContainsValue_CategoryFormat()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(currency: "BADCCY")], DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "currency");
        issue.Category.Should().Be(IssueCategory.Format);
        issue.Message.Should().Contain("BADCCY");
    }

    [Fact]
    public async Task Validate_FutureTransactionDate_MessageContainsDate_CategoryFormat()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(date: "2026-01-01")], DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "transactionDate");
        issue.Category.Should().Be(IssueCategory.Format);
        issue.Message.Should().Contain("2026-01-01");
    }

    [Fact]
    public async Task Validate_ExternalIdAlreadyInDb_MessageContainsId_CategoryReference()
    {
        await using var db = CreateDb(TenantA);
        var payee = MakePayee(TenantA, "EMP001");
        db.Payees.Add(payee);
        db.CompensationTransactions.Add(CompensationTransaction.Ingest(
            TenantA, "TXN-OLD", payee.Id, Money.Of(100m, "USD"),
            new DateOnly(2024, 1, 1), TransactionSource.EtlImport,
            "system", Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(),
            externalId: "EXT-CAT-TEST"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync(
            [ValidRow(refNum: "TXN-NEW", extId: "EXT-CAT-TEST")],
            DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "externalId");
        issue.Category.Should().Be(IssueCategory.Reference);
        issue.Message.Should().Contain("EXT-CAT-TEST");
    }

    [Fact]
    public async Task Validate_MissingPayeeCode_CategoryRequired()
    {
        await using var db = CreateDb(TenantA);
        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow(code: "")], DefaultMapping());

        var issue = results[0].Issues.First(i => i.Field == "payeeCode");
        issue.Category.Should().Be(IssueCategory.Required);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Happy path
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_AllValid_ReturnsNoIssues()
    {
        await using var db = CreateDb(TenantA);
        db.Payees.Add(MakePayee(TenantA, "EMP001"));
        await db.SaveChangesAsync();

        var sut = new TransactionImportValidationService(db, ClockAt(Now));
        var results = await sut.ValidateAsync([ValidRow()], DefaultMapping());

        results[0].HasErrors.Should().BeFalse();
        results[0].HasWarnings.Should().BeFalse();
    }
}
