using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.Handlers.Transactions;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Enrichment;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Infrastructure.Persistence;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The manual ingest now captures an explicit Category. Precedence mirrors the CRM path
/// (CrmDealReconciler): an explicit value WINS over the SKU/name resolver; a blank one still runs the
/// resolver; nothing anywhere → null. Category is never required.
/// </summary>
public sealed class IngestTransactionCategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TxDate = new(2026, 6, 15);

    private sealed record Harness(ApplicationDbContext Db, IngestTransactionHandler Handler, Guid PayeeId);

    private static Harness BuildHarness(string dbName, FakeTransactionEnrichmentService enrichment)
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var payee = Payee.Create(
            tenantId, "Test Payee", "E1", "payee@test.com", null, "seed", Guid.NewGuid(), Now);
        db.Payees.Add(payee);
        db.SaveChanges();

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin");

        var fieldRequirements = Substitute.For<IFieldRequirementService>();
        fieldRequirements.IsRequiredAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var credits = Substitute.For<ICreditAllocationService>();
        credits.AllocateAsync(Arg.Any<CompensationTransaction>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Credit>());

        var handler = new IngestTransactionHandler(
            db, tenantCtx, currentUser, new FakeClock(Now.UtcDateTime), new FakeGuidGenerator(),
            Substitute.For<IAuthorizationService>(), fieldRequirements, credits,
            enrichment, new TransactionCreateGuard(db));

        return new Harness(db, handler, payee.Id);
    }

    private static IngestTransactionCommand Command(
        Guid payeeId,
        string reference = "REF-1",
        string? category = null,
        string? productSku = null,
        string? productName = null) =>
        new(ReferenceNumber: reference,
            PayeeId: payeeId,
            Amount: 50m,
            Currency: "EUR",
            TransactionDate: TxDate,
            Quantity: 1,
            ProcessImmediately: false,
            ProductName: productName,
            ProductSku: productSku,
            Category: category);

    // An explicit category is persisted verbatim (normalized).
    [Fact]
    public async Task Explicit_category_is_persisted()
    {
        var h = BuildHarness(nameof(Explicit_category_is_persisted), new FakeTransactionEnrichmentService());

        var result = await h.Handler.Handle(Command(h.PayeeId, category: "Laptops"), default);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.CompensationTransactions.SingleAsync()).Category.Should().Be("Laptops");
    }

    // No explicit category, but a SKU that the lookup maps → the resolver still fills it (unchanged).
    [Fact]
    public async Task No_explicit_category_falls_back_to_the_resolver()
    {
        var enrichment = new FakeTransactionEnrichmentService()
            .Add(CategoryMapping.Fields.ProductSku, "LAP-12", "Laptops");
        var h = BuildHarness(nameof(No_explicit_category_falls_back_to_the_resolver), enrichment);

        var result = await h.Handler.Handle(Command(h.PayeeId, productSku: "LAP-12"), default);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.CompensationTransactions.SingleAsync()).Category.Should().Be("Laptops");
    }

    // The key precedence rule: an explicit category WINS over what the resolver would have produced.
    [Fact]
    public async Task Explicit_category_wins_over_the_resolver()
    {
        var enrichment = new FakeTransactionEnrichmentService()
            .Add(CategoryMapping.Fields.ProductSku, "LAP-12", "Laptops");
        var h = BuildHarness(nameof(Explicit_category_wins_over_the_resolver), enrichment);

        var result = await h.Handler.Handle(
            Command(h.PayeeId, category: "Servers", productSku: "LAP-12"), default);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.CompensationTransactions.SingleAsync()).Category.Should().Be("Servers");
    }

    // No category, no matching mapping → null. Category is optional; the transaction still ingests.
    [Fact]
    public async Task No_category_and_no_mapping_leaves_it_null()
    {
        var h = BuildHarness(nameof(No_category_and_no_mapping_leaves_it_null), new FakeTransactionEnrichmentService());

        var result = await h.Handler.Handle(Command(h.PayeeId), default);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.CompensationTransactions.SingleAsync()).Category.Should().BeNull();
    }

    // A blank explicit category is treated as "not provided" → resolver runs (and here finds nothing).
    [Fact]
    public async Task Blank_explicit_category_is_treated_as_absent()
    {
        var enrichment = new FakeTransactionEnrichmentService()
            .Add(CategoryMapping.Fields.ProductName, "Laptop Dell XPS", "Laptops");
        var h = BuildHarness(nameof(Blank_explicit_category_is_treated_as_absent), enrichment);

        var result = await h.Handler.Handle(
            Command(h.PayeeId, category: "   ", productName: "Laptop Dell XPS"), default);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.CompensationTransactions.SingleAsync()).Category.Should().Be("Laptops");
    }
}
