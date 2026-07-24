using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Compensation.Enrichment;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services.Crm;
using Wasnie.Infrastructure.Services.HubSpot;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Integrations;

/// <summary>
/// WI-CRM-CATEGORY: the CRM-carried category takes precedence over the manual lookup table, and the
/// tenant-declared property is only requested from HubSpot when configured.
/// </summary>
public sealed class CrmCategoryFromCrmTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static (ApplicationDbContext Db, CrmDealReconciler Reconciler) Build(
        Guid tenantId, FakeTransactionEnrichmentService enrichment)
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"crm-cat-{Guid.NewGuid()}").Options,
            tenantCtx,
            Substitute.For<MediatR.IPublisher>());

        var clock = new FakeClock(Now.UtcDateTime);
        var guid = new FakeGuidGenerator();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("user-1");

        var resolver = new CrmOwnerResolver(db, clock, guid, currentUser);
        var createGuard = new Wasnie.Application.Compensation.Common.TransactionCreateGuard(db);
        var driftPolicy = new Wasnie.Application.Integrations.Crm.Drift.CrmDriftPolicy(db, guid);
        var reconciler = new CrmDealReconciler(db, guid, resolver, createGuard, driftPolicy, enrichment);

        return (db, reconciler);
    }

    // One EUR deal with a single line item that reconciles to the deal amount.
    private static CrmDeal DealWithLine(string? sku, string? crmCategory) =>
        new("D1", "Deal 1", 1000m, "EUR", new DateOnly(2026, 7, 1), null,
        [
            new CrmLineItem("li1", "LAP-12", 1m, 1000m, 1000m, Sku: sku, CategoryFromCrm: crmCategory),
        ]);

    private static async Task<Wasnie.Domain.Compensation.Transactions.CompensationTransaction> ReconcileSingleAsync(
        ApplicationDbContext db, CrmDealReconciler reconciler, Guid tenantId, CrmDeal deal)
    {
        await reconciler.ReconcileAsync(
            tenantId, "HubSpot", [deal], Array.Empty<CrmOwner>(), "EUR", "actor", "a@b", Now, default);
        return await db.CompensationTransactions.SingleAsync();
    }

    // (a) No CRM category (property not configured → arrives null) and a lookup mapping exists → the lookup
    // table still applies. Current behaviour, no regression.
    [Fact]
    public async Task No_crm_category_falls_back_to_lookup_table()
    {
        var tenantId = Guid.NewGuid();
        var enrichment = new FakeTransactionEnrichmentService().Add(CategoryMapping.Fields.ProductSku, "LAP-12", "Laptops");
        var (db, reconciler) = Build(tenantId, enrichment);

        var tx = await ReconcileSingleAsync(db, reconciler, tenantId, DealWithLine(sku: "LAP-12", crmCategory: null));

        tx.Category.Should().Be("Laptops");
    }

    // (b) CRM brings a category → it WINS over whatever the lookup table would have produced.
    [Fact]
    public async Task Crm_category_wins_over_the_lookup_table()
    {
        var tenantId = Guid.NewGuid();
        // The lookup would map LAP-12 → Laptops, but the CRM says Servers → Servers must win.
        var enrichment = new FakeTransactionEnrichmentService().Add(CategoryMapping.Fields.ProductSku, "LAP-12", "Laptops");
        var (db, reconciler) = Build(tenantId, enrichment);

        var tx = await ReconcileSingleAsync(db, reconciler, tenantId, DealWithLine(sku: "LAP-12", crmCategory: "Servers"));

        tx.Category.Should().Be("Servers");
    }

    // (c) CRM value blank but the lookup table matches → uses the lookup.
    [Fact]
    public async Task Blank_crm_category_falls_back_to_lookup_table()
    {
        var tenantId = Guid.NewGuid();
        var enrichment = new FakeTransactionEnrichmentService().Add(CategoryMapping.Fields.ProductSku, "LAP-12", "Laptops");
        var (db, reconciler) = Build(tenantId, enrichment);

        var tx = await ReconcileSingleAsync(db, reconciler, tenantId, DealWithLine(sku: "LAP-12", crmCategory: "   "));

        tx.Category.Should().Be("Laptops");
    }

    // (d) Neither CRM nor lookup match → Category null, and ingest does NOT fail.
    [Fact]
    public async Task No_crm_and_no_lookup_leaves_category_null_and_ingests()
    {
        var tenantId = Guid.NewGuid();
        var enrichment = new FakeTransactionEnrichmentService(); // empty lookup
        var (db, reconciler) = Build(tenantId, enrichment);

        var tx = await ReconcileSingleAsync(db, reconciler, tenantId, DealWithLine(sku: "NOT-MAPPED", crmCategory: null));

        tx.Category.Should().BeNull();
        tx.Amount.Amount.Should().Be(1000m);
    }

    // (f) A deal WITHOUT line items → one deal-level transaction, Category null, no regression.
    [Fact]
    public async Task Deal_without_line_items_still_ingests_with_null_category()
    {
        var tenantId = Guid.NewGuid();
        var enrichment = new FakeTransactionEnrichmentService().Add(CategoryMapping.Fields.ProductSku, "LAP-12", "Laptops");
        var (db, reconciler) = Build(tenantId, enrichment);

        var deal = new CrmDeal("D9", "No Lines", 500m, "EUR", new DateOnly(2026, 7, 1), null);
        var tx = await ReconcileSingleAsync(db, reconciler, tenantId, deal);

        tx.Category.Should().BeNull();
        tx.ReferenceNumber.Should().Be("HUBSPOT-D9");
    }

    // (e) The tenant-declared property is requested from HubSpot ONLY when configured.
    [Fact]
    public void Configured_property_is_added_to_the_fetch_only_when_set()
    {
        HubSpotCrmDealSource.BuildLineItemProperties(null)
            .Should().BeEquivalentTo(["quantity", "price", "amount", "name", "hs_sku"]);

        HubSpotCrmDealSource.BuildLineItemProperties("")
            .Should().NotContain("");

        var withProp = HubSpotCrmDealSource.BuildLineItemProperties("product_category");
        withProp.Should().Contain("product_category");
        withProp.Should().Contain("hs_sku"); // defaults still there
    }
}
