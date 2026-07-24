using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Application.Integrations.HubSpot;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Integrations.Crm;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Crm;
using Wasnie.UnitTests.TestDoubles;
using IAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// FASE 2c money-path tests: deals → transactions with correct payee resolution and idempotency.
/// Uses the REAL <see cref="CrmOwnerResolver"/> over an in-memory DB and a mocked <see cref="ICrmDealSource"/>
/// (the HTTP boundary), so it exercises import + resolution + idempotency together.
/// </summary>
public sealed class ImportHubSpotDealsHandlerTests
{
    private const string Source = "HubSpot";
    private static readonly DateTime Now = new(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public required ApplicationDbContext Db { get; init; }
        public required ImportHubSpotDealsHandler Handler { get; init; }
        public required ICrmDealSource DealSource { get; init; }
        public required Guid TenantId { get; init; }
    }

    private static Harness BuildHarness(string dbName, Guid tenantId)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var clock = new FakeClock(Now);
        var guid = new FakeGuidGenerator();

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-1");

        var authz = Substitute.For<IAuthorizationService>();

        var dealSource = Substitute.For<ICrmDealSource>();
        dealSource.SourceName.Returns(Source);

        var resolver = new CrmOwnerResolver(db, clock, guid, currentUser);
        var createGuard = new Wasnie.Application.Compensation.Common.TransactionCreateGuard(db);
        var driftPolicy = new Wasnie.Application.Integrations.Crm.Drift.CrmDriftPolicy(db, guid);
        var reconciler = new Wasnie.Application.Integrations.Crm.CrmDealReconciler(
            db, guid, resolver, createGuard, driftPolicy,
            new Wasnie.UnitTests.TestDoubles.FakeTransactionEnrichmentService());

        var handler = new ImportHubSpotDealsHandler(
            tenantCtx, currentUser, clock, authz, dealSource, reconciler);

        return new Harness { Db = db, Handler = handler, DealSource = dealSource, TenantId = tenantId };
    }

    private static void SetupSource(
        ICrmDealSource source, Guid tenantId,
        IReadOnlyList<CrmDeal> deals, IReadOnlyList<CrmOwner> owners, string? currency = "USD")
    {
        source.GetClosedWonDealsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(deals);
        source.GetOwnersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(owners);
        source.GetDefaultCurrencyAsync(tenantId, Arg.Any<CancellationToken>()).Returns(currency);
    }

    private static Payee SeedPayee(ApplicationDbContext db, Guid tenantId, string code, string? email)
    {
        var payee = Payee.Create(
            tenantId, $"Payee {code}", code, email, null, "seed", Guid.NewGuid(), new DateTimeOffset(Now));
        db.Payees.Add(payee);
        db.SaveChanges();
        return payee;
    }

    [Fact]
    public async Task Deal_with_owner_matching_by_email_creates_transaction_assigned_to_that_payee()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_with_owner_matching_by_email_creates_transaction_assigned_to_that_payee), tenantId);
        var payee = SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("101", "Big Deal", 5000m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "Alice@Example.com", "Alice", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().Be(1);
        result.Value.AssignedToPayee.Should().Be(1);
        result.Value.NewOwnerMappings.Should().Be(1);

        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.PayeeId.Should().Be(payee.Id);
        tx.Amount.Amount.Should().Be(5000m);
        tx.Amount.Currency.Should().Be("USD");
        tx.TransactionDate.Should().Be(new DateOnly(2026, 6, 1));
        tx.Source.Should().Be(TransactionSource.CrmSync);
        tx.ExternalId.Should().Be("101");
        tx.Status.Should().Be(CompensationTransactionStatus.Pending);

        // The email auto-match persisted a stable mapping for future imports.
        var mapping = await h.Db.CrmOwnerMappings.SingleAsync();
        mapping.CrmOwnerId.Should().Be("O1");
        mapping.PayeeId.Should().Be(payee.Id);
        mapping.MatchMethod.Should().Be(CrmOwnerMatchMethod.Email);
    }

    [Fact]
    public async Task Deal_with_owner_that_has_no_matching_payee_creates_unassigned_transaction()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_with_owner_that_has_no_matching_payee_creates_unassigned_transaction), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "someone-else@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("202", "Orphan Deal", 1200m, "EUR", new DateOnly(2026, 5, 10), "O9") },
            owners: new[] { new CrmOwner("O9", "bob@example.com", "Bob", "B", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().Be(1);
        result.Value.Unassigned.Should().Be(1);
        result.Value.NewOwnerMappings.Should().Be(0);

        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.PayeeId.Should().BeNull();
        tx.Amount.Currency.Should().Be("EUR");
        (await h.Db.CrmOwnerMappings.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Re_importing_a_deal_whose_transaction_was_voided_creates_a_new_one()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Re_importing_a_deal_whose_transaction_was_voided_creates_a_new_one), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("101", "Big Deal", 5000m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "Alice", "A", false) });

        var first = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        first.Value!.Created.Should().Be(1);

        // The imported transaction is voided (e.g. wrong currency).
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.Cancel("wrong currency", "user-1", new DateTimeOffset(Now), Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        // Re-importing the SAME deal now creates a NEW transaction (Opción B); the void stays as history.
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        second.Value!.Created.Should().Be(1);
        second.Value.SkippedAlreadyImported.Should().Be(0);

        (await h.Db.CompensationTransactions.CountAsync()).Should().Be(2);
        (await h.Db.CompensationTransactions.CountAsync(t => t.Status == CompensationTransactionStatus.Cancelled))
            .Should().Be(1);
        (await h.Db.CompensationTransactions.CountAsync(t => t.Status == CompensationTransactionStatus.Pending))
            .Should().Be(1);
    }

    [Fact]
    public async Task Deal_with_no_owner_creates_unassigned_transaction()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_with_no_owner_creates_unassigned_transaction), tenantId);

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("303", "No Owner", 999m, "USD", new DateOnly(2026, 4, 1), null) },
            owners: Array.Empty<CrmOwner>());

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Unassigned.Should().Be(1);
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.PayeeId.Should().BeNull();
    }

    [Fact]
    public async Task Re_importing_the_same_deal_does_not_create_a_duplicate_transaction()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Re_importing_the_same_deal_does_not_create_a_duplicate_transaction), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("101", "Big Deal", 5000m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "Alice", "A", false) });

        var first = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        first.Value!.Created.Should().Be(1);

        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        second.Value!.Created.Should().Be(0);
        second.Value.SkippedAlreadyImported.Should().Be(1);
        second.Value.NewOwnerMappings.Should().Be(0); // mapping already exists from the first run

        (await h.Db.CompensationTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Import_is_tenant_isolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string dbName = nameof(Import_is_tenant_isolated);

        // Same owner email exists as a different payee in each tenant (email is unique PER tenant).
        var a = BuildHarness(dbName, tenantA);
        var payeeA = SeedPayee(a.Db, tenantA, "A1", "shared@example.com");
        SetupSource(a.DealSource, tenantA,
            deals: new[] { new CrmDeal("777", "Deal", 100m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "shared@example.com", "S", "A", false) });

        var b = BuildHarness(dbName, tenantB);
        var payeeB = SeedPayee(b.Db, tenantB, "B1", "shared@example.com");
        SetupSource(b.DealSource, tenantB,
            deals: new[] { new CrmDeal("777", "Deal", 100m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "shared@example.com", "S", "B", false) });

        await a.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        await b.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        // Each tenant sees only its own transaction, assigned to its own payee.
        var txA = await a.Db.CompensationTransactions.SingleAsync();
        txA.TenantId.Should().Be(tenantA);
        txA.PayeeId.Should().Be(payeeA.Id);

        var txB = await b.Db.CompensationTransactions.SingleAsync();
        txB.TenantId.Should().Be(tenantB);
        txB.PayeeId.Should().Be(payeeB.Id);

        // Mappings are isolated too.
        (await a.Db.CrmOwnerMappings.SingleAsync()).PayeeId.Should().Be(payeeA.Id);
        (await b.Db.CrmOwnerMappings.SingleAsync()).PayeeId.Should().Be(payeeB.Id);
    }

    [Fact]
    public async Task Deal_without_currency_falls_back_to_account_default_currency()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_without_currency_falls_back_to_account_default_currency), tenantId);

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("404", "No Currency", 250m, null, new DateOnly(2026, 6, 1), null) },
            owners: Array.Empty<CrmOwner>(),
            currency: "GBP");

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().Be(1);
        (await h.Db.CompensationTransactions.SingleAsync()).Amount.Currency.Should().Be("GBP");
    }

    [Fact]
    public async Task Deal_with_no_amount_is_skipped_as_invalid()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_with_no_amount_is_skipped_as_invalid), tenantId);

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("505", "No Amount", null, "USD", new DateOnly(2026, 6, 1), null) },
            owners: Array.Empty<CrmOwner>());

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().Be(0);
        result.Value.SkippedInvalid.Should().Be(1);
        (await h.Db.CompensationTransactions.AnyAsync()).Should().BeFalse();
    }

    // ── Line items → one transaction per line, with real quantity (WI-PROD-QUANTITY) ──────────────

    // (a) one line item of N units → a transaction whose Quantity is N (not 1).
    [Fact]
    public async Task Deal_with_one_line_item_creates_transaction_with_real_quantity()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_with_one_line_item_creates_transaction_with_real_quantity), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("101", "Bulk", 50000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[] { new CrmLineItem("li1", "Widget", 5000m, 10m, 50000m) }) },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(1);
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.Quantity.Should().Be(5000);
        tx.Amount.Amount.Should().Be(50000m);
        tx.ReferenceNumber.Should().Be("HUBSPOT-101-li1");
        tx.ExternalId.Should().Be("101-li1");
    }

    // (b) M line items → M transactions, each with its own quantity/amount (no averaged unit price).
    [Fact]
    public async Task Deal_with_multiple_line_items_creates_one_transaction_each()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_with_multiple_line_items_creates_one_transaction_each), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("102", "Multi", 65000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[]
                {
                    new CrmLineItem("a", "Widget", 5000m, 10m, 50000m),
                    new CrmLineItem("b", "Gadget", 300m, 50m, 15000m),
                }) },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(2);
        var txs = await h.Db.CompensationTransactions.ToListAsync();
        txs.Should().HaveCount(2);
        txs.Single(t => t.ExternalId == "102-a").Quantity.Should().Be(5000);
        txs.Single(t => t.ExternalId == "102-a").Amount.Amount.Should().Be(50000m);
        txs.Single(t => t.ExternalId == "102-b").Quantity.Should().Be(300);
        txs.Single(t => t.ExternalId == "102-b").Amount.Amount.Should().Be(15000m);
    }

    // (d) Σ(line items) ≠ deal amount → flagged (TotalsMismatch), NOT ingested silently.
    [Fact]
    public async Task Deal_whose_line_items_do_not_sum_to_deal_amount_is_flagged_not_ingested()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_whose_line_items_do_not_sum_to_deal_amount_is_flagged_not_ingested), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        // Line items sum to 65000 but the deal amount is 60000 (a deal-level discount) → do not guess.
        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("103", "Discounted", 60000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[]
                {
                    new CrmLineItem("x", "Widget", 5000m, 10m, 50000m),
                    new CrmLineItem("y", "Gadget", 300m, 50m, 15000m),
                }) },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(0);
        result.Value.TotalsMismatch.Should().Be(1);
        (await h.Db.CompensationTransactions.AnyAsync()).Should().BeFalse();
    }

    // (f) re-syncing the same deal with line items does NOT duplicate (idempotency per line item id).
    [Fact]
    public async Task Re_syncing_a_deal_with_line_items_does_not_duplicate()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Re_syncing_a_deal_with_line_items_does_not_duplicate), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("102", "Multi", 65000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[]
                {
                    new CrmLineItem("a", "Widget", 5000m, 10m, 50000m),
                    new CrmLineItem("b", "Gadget", 300m, 50m, 15000m),
                }) },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var first = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        first.Value!.Created.Should().Be(2);

        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);
        second.Value!.Created.Should().Be(0);
        (await h.Db.CompensationTransactions.CountAsync()).Should().Be(2);
    }

    // (g) a line item's amount changing in HubSpot is detected as drift per line and (Pending) re-created.
    [Fact]
    public async Task Line_item_amount_change_is_detected_as_drift_per_line()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Line_item_amount_change_is_detected_as_drift_per_line), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");
        var owners = new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) };
        h.DealSource.GetOwnersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(owners);
        h.DealSource.GetDefaultCurrencyAsync(tenantId, Arg.Any<CancellationToken>()).Returns("USD");

        h.DealSource.GetClosedWonDealsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CrmDeal("104", "D", 50000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[] { new CrmLineItem("li", "Widget", 5000m, 10m, 50000m) }),
        });
        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        // The line item's amount changed in HubSpot (deal total moves with it to keep the totals reconciled).
        h.DealSource.GetClosedWonDealsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CrmDeal("104", "D", 60000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[] { new CrmLineItem("li", "Widget", 5000m, 12m, 60000m) }),
        });
        var second = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        second.Value!.DriftAutoResolved.Should().Be(1);
        var active = await h.Db.CompensationTransactions.SingleAsync(t => t.Status == CompensationTransactionStatus.Pending);
        active.Amount.Amount.Should().Be(60000m);
        active.ExternalId.Should().Be("104-li");
    }

    // ── Deal name persisted as the transaction Description (WI readable transaction name) ──────────

    // A deal WITHOUT line items → the single transaction carries the deal name.
    [Fact]
    public async Task Deal_without_line_items_persists_deal_name_as_description()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_without_line_items_persists_deal_name_as_description), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("901", "Contrato Acme 2026", 5000m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(1);
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.Description.Should().Be("Contrato Acme 2026");
    }

    // A deal WITH line items → every line transaction carries the SAME deal name; the rows are told
    // apart by their per-line reference, never by the label.
    [Fact]
    public async Task Deal_with_line_items_persists_same_deal_name_on_every_transaction()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_with_line_items_persists_same_deal_name_on_every_transaction), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("902", "Contrato Acme 2026", 65000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[]
                {
                    new CrmLineItem("a", "Widget", 5000m, 10m, 50000m),
                    new CrmLineItem("b", "Gadget", 300m, 50m, 15000m),
                }) },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(2);
        var txs = await h.Db.CompensationTransactions.ToListAsync();
        txs.Should().OnlyContain(t => t.Description == "Contrato Acme 2026");
        txs.Select(t => t.ReferenceNumber).Should().BeEquivalentTo(["HUBSPOT-902-a", "HUBSPOT-902-b"]);
    }

    // ── What was sold: the line item's own product data (Paso 4a) ─────────────────────────────

    // The link that was broken: the line item's name and SKU were fetched and then thrown away, with
    // the DEAL name persisted in their place. Description keeps saying WHICH SALE; the product fields
    // say WHAT was sold, so a deal mixing two economies keeps them apart.
    [Fact]
    public async Task Line_item_name_and_sku_are_persisted_alongside_the_deal_name()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Line_item_name_and_sku_are_persisted_alongside_the_deal_name), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("910", "Contrato Acme 2026", 65000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[]
                {
                    new CrmLineItem("a", "Industrial Press 3000", 1m, 50000m, 50000m, Sku: "MCH-0042"),
                    new CrmLineItem("b", "Installation Course", 1m, 15000m, 15000m, Sku: "CRS-0007"),
                }) },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(2);
        var txs = await h.Db.CompensationTransactions.ToListAsync();

        // Both lines share the sale label...
        txs.Should().OnlyContain(t => t.Description == "Contrato Acme 2026");

        // ...but carry their own product identity.
        var machine = txs.Single(t => t.ExternalId == "910-a");
        machine.ProductName.Should().Be("Industrial Press 3000");
        machine.ProductSku.Should().Be("MCH-0042");

        var course = txs.Single(t => t.ExternalId == "910-b");
        course.ProductName.Should().Be("Installation Course");
        course.ProductSku.Should().Be("CRS-0007");
    }

    // A line item with no catalogued product is normal — null, and the sale still ingests.
    [Fact]
    public async Task A_line_item_without_a_sku_still_ingests_with_null_product_fields()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(A_line_item_without_a_sku_still_ingests_with_null_product_fields), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("911", "Contrato Sin Catalogo", 5000m, "USD", new DateOnly(2026, 6, 1), "O1",
                new[] { new CrmLineItem("li", null, 1m, 5000m, 5000m) }) },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(1);
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.ProductName.Should().BeNull();
        tx.ProductSku.Should().BeNull();
        tx.Amount.Amount.Should().Be(5000m);
    }

    // A deal WITHOUT line items has no product to speak of — unchanged behaviour, no regression.
    [Fact]
    public async Task A_deal_without_line_items_keeps_the_deal_name_and_no_product()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(A_deal_without_line_items_keeps_the_deal_name_and_no_product), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("912", "Deal Sin Lineas", 5000m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.Description.Should().Be("Deal Sin Lineas");
        tx.ProductName.Should().BeNull();
        tx.ProductSku.Should().BeNull();
    }

    // A deal with NO name must still ingest — the label is descriptive, never a precondition.
    [Fact]
    public async Task Deal_without_a_name_still_ingests_with_null_description()
    {
        var tenantId = Guid.NewGuid();
        var h = BuildHarness(nameof(Deal_without_a_name_still_ingests_with_null_description), tenantId);
        SeedPayee(h.Db, tenantId, "E1", "alice@example.com");

        SetupSource(h.DealSource, tenantId,
            deals: new[] { new CrmDeal("903", null, 5000m, "USD", new DateOnly(2026, 6, 1), "O1") },
            owners: new[] { new CrmOwner("O1", "alice@example.com", "A", "A", false) });

        var result = await h.Handler.Handle(new ImportHubSpotDealsCommand(), default);

        result.Value!.Created.Should().Be(1);
        var tx = await h.Db.CompensationTransactions.SingleAsync();
        tx.Description.Should().BeNull();
        tx.Amount.Amount.Should().Be(5000m);
    }
}
