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
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// PASO 4 — EXCEL source uses the same centralized rule: importing a row whose reference exists only as a
/// Void creates a new transaction (Opción B); the void stays as history.
/// </summary>
public sealed class ExcelImportReimportTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ApplicationDbContext NewDb(string name)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(TenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name)
                // The job opens a DB transaction per chunk; InMemory has no transactions → make it a no-op.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            ctx, Substitute.For<MediatR.IPublisher>());
    }

    [Fact]
    public async Task Re_importing_a_reference_that_only_exists_as_a_void_creates_a_new_transaction()
    {
        var db = NewDb(nameof(Re_importing_a_reference_that_only_exists_as_a_void_creates_a_new_transaction));

        // A previously-imported Excel row, now voided.
        var voided = CompensationTransaction.Ingest(TenantId, "EXC-1", null, Money.Of(50m, "USD"),
            new DateOnly(2026, 5, 1), TransactionSource.EtlImport, "seed", Guid.NewGuid(), Now, Guid.NewGuid());
        voided.Cancel("wrong currency", "seed", Now, Guid.NewGuid());
        db.CompensationTransactions.Add(voided);
        await db.SaveChangesAsync();

        var mapping = new TransactionImportColumnMapping
        {
            ReferenceNumberColumn = "ref",
            PayeeCodeColumn = "payee",
            AmountColumn = "amount",
            CurrencyColumn = "currency",
            TransactionDateColumn = "date",
        };
        var row = new Dictionary<string, string>
        {
            ["ref"] = "EXC-1", ["payee"] = "", ["amount"] = "100", ["currency"] = "EUR", ["date"] = "2026-06-01",
        };

        var validator = Substitute.For<ITransactionImportValidationService>();
        validator.ValidateAsync(Arg.Any<List<Dictionary<string, string>>>(),
                Arg.Any<TransactionImportColumnMapping>(), Arg.Any<CancellationToken>())
            .Returns(new List<TransactionRowValidationResult>
            {
                new() { RowNumber = 1, OriginalData = row, Issues = [] },
            });

        var creditAlloc = Substitute.For<ICreditAllocationService>();
        creditAlloc.AllocateAsync(Arg.Any<CompensationTransaction>(), Arg.Any<CancellationToken>())
            .Returns(new List<Credit>());

        var handler = new TransactionImportJobHandler(
            db, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(), validator, creditAlloc,
            new TransactionCreateGuard(db), NullLogger<TransactionImportJobHandler>.Instance);

        var payload = new TransactionImportPayload(
            TenantId, "user-1", "user@test", "file.xlsx", mapping,
            new TransactionImportOptions(SkipRowsWithWarnings: false),
            new List<Dictionary<string, string>> { row });

        var context = new JobContext(Guid.NewGuid(), Substitute.For<IBackgroundJobService>());

        await handler.HandleAsync(payload, context, default);

        (await db.CompensationTransactions.CountAsync()).Should().Be(2); // void kept + new active
        (await db.CompensationTransactions.CountAsync(t => t.Status == CompensationTransactionStatus.Cancelled))
            .Should().Be(1);
        var active = await db.CompensationTransactions
            .SingleAsync(t => t.Status != CompensationTransactionStatus.Cancelled);
        active.ReferenceNumber.Should().Be("EXC-1");
        active.Amount.Currency.Should().Be("EUR");
    }
}
