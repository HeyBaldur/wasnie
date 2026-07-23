using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.BackgroundJobs;

/// <summary>
/// Job-level coverage for the two Excel paths, over an in-memory DB (no Docker).
/// Covers the wiring the wizards depend on: the import job honouring a mapped Quantity column, and
/// the update job propagating Description without letting a relabel disturb calculated money.
/// </summary>
public sealed class TransactionExcelJobsTests
{
    private static readonly DateTime Now = new(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext BuildDb(string dbName, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                // The jobs wrap each chunk in a DB transaction; the in-memory provider has no
                // transactions and warns instead. Ignoring the warning lets the job logic run
                // unchanged — chunk atomicity itself is a provider concern, not what's under test.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());
    }

    private static JobContext BuildJobContext() =>
        new(Guid.NewGuid(), Substitute.For<IBackgroundJobService>());

    // ── Import job: Quantity column ───────────────────────────────────────────────────────────

    private static TransactionImportJobHandler BuildImportHandler(ApplicationDbContext db)
    {
        // Validation is exercised directly in TransactionFieldValidatorsTests; here every row is
        // declared valid so the test isolates what the JOB does with the mapped columns.
        var validator = Substitute.For<ITransactionImportValidationService>();
        validator
            .ValidateAsync(Arg.Any<List<Dictionary<string, string>>>(),
                Arg.Any<TransactionImportColumnMapping>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((List<Dictionary<string, string>>)ci[0])
                .Select((r, i) => new TransactionRowValidationResult
                {
                    RowNumber = i + 1,
                    OriginalData = r,
                    Issues = [],
                })
                .ToList());

        var credits = Substitute.For<ICreditAllocationService>();
        credits.AllocateAsync(Arg.Any<CompensationTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Credit>());

        return new TransactionImportJobHandler(
            db, new FakeClock(Now), new FakeGuidGenerator(), validator, credits,
            new TransactionCreateGuard(db), NullLogger<TransactionImportJobHandler>.Instance);
    }

    private static TransactionImportPayload BuildImportPayload(
        Guid tenantId, Dictionary<string, string> row, string? quantityColumn)
    {
        return new TransactionImportPayload(
            TenantId: tenantId,
            RequestedByUserId: "user-1",
            RequestedByEmail: "user@test.com",
            OriginalFileName: "tx.xlsx",
            ColumnMapping: new TransactionImportColumnMapping
            {
                ReferenceNumberColumn = "Reference",
                AmountColumn = "Amount",
                CurrencyColumn = "Currency",
                TransactionDateColumn = "Date",
                QuantityColumn = quantityColumn,
            },
            Rows: [row],
            Options: new TransactionImportOptions(SkipRowsWithWarnings: false));
    }

    // (a) Column mapped → the real quantity lands on the transaction, not the default.
    [Fact]
    public async Task Import_with_quantity_column_mapped_uses_the_real_quantity()
    {
        var tenantId = Guid.NewGuid();
        var db = BuildDb(nameof(Import_with_quantity_column_mapped_uses_the_real_quantity), tenantId);
        var row = new Dictionary<string, string>
        {
            ["Reference"] = "REF-QTY-1", ["Amount"] = "5000", ["Currency"] = "USD",
            ["Date"] = "2026-06-01", ["Units"] = "50",
        };

        await BuildImportHandler(db).HandleAsync(
            BuildImportPayload(tenantId, row, quantityColumn: "Units"), BuildJobContext(), default);

        var tx = await db.CompensationTransactions.SingleAsync();
        tx.Quantity.Should().Be(50);
    }

    // (b) Column NOT mapped → default of 1; files that predate the column keep importing.
    [Fact]
    public async Task Import_without_quantity_column_falls_back_to_default_of_one()
    {
        var tenantId = Guid.NewGuid();
        var db = BuildDb(nameof(Import_without_quantity_column_falls_back_to_default_of_one), tenantId);
        var row = new Dictionary<string, string>
        {
            ["Reference"] = "REF-QTY-2", ["Amount"] = "5000", ["Currency"] = "USD",
            ["Date"] = "2026-06-01",
        };

        await BuildImportHandler(db).HandleAsync(
            BuildImportPayload(tenantId, row, quantityColumn: null), BuildJobContext(), default);

        var tx = await db.CompensationTransactions.SingleAsync();
        tx.Quantity.Should().Be(1);
    }

    // ── Update job: Description ───────────────────────────────────────────────────────────────

    private static UpdateTransactionsFromExcelJobHandler BuildUpdateHandler(ApplicationDbContext db) =>
        new(db, new FakeClock(Now), new FakeGuidGenerator(),
            NullLogger<UpdateTransactionsFromExcelJobHandler>.Instance);

    private static TransactionUpdatePayload BuildUpdatePayload(
        Guid tenantId, Dictionary<string, string> row) =>
        new(TenantId: tenantId,
            RequestedByUserId: "user-1",
            RequestedByEmail: "user@test.com",
            OriginalFileName: "tx-update.xlsx",
            ColumnMapping: new TransactionUpdateColumnMapping(
                ReferenceNumberColumn: "Reference",
                AmountColumn: null,
                CurrencyColumn: null,
                TransactionDateColumn: null,
                PayeeCodeColumn: null,
                QuantityColumn: null,
                DescriptionColumn: "Description"),
            Rows: [row]);

    private static CompensationTransaction SeedTransaction(
        ApplicationDbContext db, Guid tenantId, string reference, string? description)
    {
        var tx = CompensationTransaction.Ingest(
            tenantId, reference, Guid.NewGuid(), Money.Of(1000m, "USD"), new DateOnly(2026, 6, 1),
            TransactionSource.EtlImport, "seed", Guid.NewGuid(), new DateTimeOffset(Now),
            Guid.NewGuid(), description: description);
        db.CompensationTransactions.Add(tx);
        db.SaveChanges();
        return tx;
    }

    // (d) The gap this WI closes: export → edit the name → re-upload actually changes it.
    [Fact]
    public async Task Update_from_excel_changes_the_description()
    {
        var tenantId = Guid.NewGuid();
        var db = BuildDb(nameof(Update_from_excel_changes_the_description), tenantId);
        SeedTransaction(db, tenantId, "REF-UPD-1", "Old name");

        await BuildUpdateHandler(db).HandleAsync(
            BuildUpdatePayload(tenantId, new Dictionary<string, string>
            {
                ["Reference"] = "REF-UPD-1", ["Description"] = "  Acme Contract 2026  ",
            }),
            BuildJobContext(), default);

        var tx = await db.CompensationTransactions.SingleAsync();
        tx.Description.Should().Be("Acme Contract 2026");
    }

    // A blank cell means "no change", exactly like every other column — a re-upload must never
    // silently wipe a name the user did not touch.
    [Fact]
    public async Task Update_from_excel_with_a_blank_description_cell_leaves_the_name_untouched()
    {
        var tenantId = Guid.NewGuid();
        var db = BuildDb(nameof(Update_from_excel_with_a_blank_description_cell_leaves_the_name_untouched), tenantId);
        SeedTransaction(db, tenantId, "REF-UPD-2", "Keep me");

        await BuildUpdateHandler(db).HandleAsync(
            BuildUpdatePayload(tenantId, new Dictionary<string, string>
            {
                ["Reference"] = "REF-UPD-2", ["Description"] = "   ",
            }),
            BuildJobContext(), default);

        var tx = await db.CompensationTransactions.SingleAsync();
        tx.Description.Should().Be("Keep me");
    }

    // The core money-safety property: renaming a Calculated transaction must NOT supersede its
    // Credits nor revert it to Pending. Description carries no calculation semantics.
    [Fact]
    public async Task Description_only_update_does_not_supersede_credits_or_revert_status()
    {
        var tenantId = Guid.NewGuid();
        var db = BuildDb(nameof(Description_only_update_does_not_supersede_credits_or_revert_status), tenantId);
        var tx = SeedTransaction(db, tenantId, "REF-UPD-3", "Old name");
        tx.MarkCalculated(1, Money.Of(100m, "USD"), "seed", new DateTimeOffset(Now), Guid.NewGuid());
        db.SaveChanges();

        await BuildUpdateHandler(db).HandleAsync(
            BuildUpdatePayload(tenantId, new Dictionary<string, string>
            {
                ["Reference"] = "REF-UPD-3", ["Description"] = "Renamed",
            }),
            BuildJobContext(), default);

        var updated = await db.CompensationTransactions.SingleAsync();
        updated.Description.Should().Be("Renamed");
        updated.Status.Should().Be(CompensationTransactionStatus.Calculated);
    }

    // (e) The existing update rules still hold: a Paid transaction is skipped, Description included.
    [Fact]
    public async Task Paid_transaction_is_still_skipped_when_only_the_description_changes()
    {
        var tenantId = Guid.NewGuid();
        var db = BuildDb(nameof(Paid_transaction_is_still_skipped_when_only_the_description_changes), tenantId);
        var tx = SeedTransaction(db, tenantId, "REF-UPD-4", "Original");
        tx.MarkCalculated(1, Money.Of(100m, "USD"), "seed", new DateTimeOffset(Now), Guid.NewGuid());
        tx.MarkPaid("seed", new DateTimeOffset(Now), Guid.NewGuid());
        db.SaveChanges();

        await BuildUpdateHandler(db).HandleAsync(
            BuildUpdatePayload(tenantId, new Dictionary<string, string>
            {
                ["Reference"] = "REF-UPD-4", ["Description"] = "Should not apply",
            }),
            BuildJobContext(), default);

        var untouched = await db.CompensationTransactions.SingleAsync();
        untouched.Description.Should().Be("Original");
        untouched.Status.Should().Be(CompensationTransactionStatus.Paid);
    }
}
