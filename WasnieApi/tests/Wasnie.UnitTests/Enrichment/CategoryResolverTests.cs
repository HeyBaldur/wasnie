using FluentAssertions;
using Wasnie.Application.Compensation.Enrichment;
using Wasnie.Domain.Compensation.Enrichment;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.UnitTests.Enrichment;

/// <summary>
/// The enrichment resolver: turns a tenant lookup table into a category for a transaction. This is the
/// layer that fixes the real incident — LAP-12 arriving in ProductName while the rule filtered on SKU.
/// </summary>
public sealed class CategoryResolverTests
{
    private const string Sku = CategoryMapping.Fields.ProductSku;
    private const string Name = CategoryMapping.Fields.ProductName;

    // (a) A SKU that matches → category assigned.
    [Fact]
    public void Sku_match_assigns_the_category()
    {
        var resolver = CategoryResolver.FromMappings([(Sku, "LAP-12", "Laptops")]);
        resolver.Resolve(productSku: "LAP-12", productName: "anything").Should().Be("Laptops");
    }

    // (b) No SKU, but a ProductName that matches → category assigned via the fallback.
    [Fact]
    public void Name_match_assigns_the_category_when_no_sku()
    {
        var resolver = CategoryResolver.FromMappings([(Name, "LAP-12", "Laptops")]);
        resolver.Resolve(productSku: null, productName: "LAP-12").Should().Be("Laptops");
    }

    // (c) A SKU that does NOT match but a Name that DOES → the name is used (this is Rodolfo's case).
    [Fact]
    public void Falls_back_to_name_when_sku_does_not_match()
    {
        var resolver = CategoryResolver.FromMappings([(Name, "LAP-12", "Laptops")]);
        resolver.Resolve(productSku: "NOT-CATALOGUED", productName: "LAP-12").Should().Be("Laptops");
    }

    // SKU is tried FIRST: when both a SKU and a Name mapping could match, the SKU wins.
    [Fact]
    public void Sku_takes_precedence_over_name()
    {
        var resolver = CategoryResolver.FromMappings(
        [
            (Sku, "S-1", "FromSku"),
            (Name, "N-1", "FromName"),
        ]);
        resolver.Resolve(productSku: "S-1", productName: "N-1").Should().Be("FromSku");
    }

    // Matching mirrors the engine: trimmed + case-insensitive.
    [Fact]
    public void Matching_is_case_insensitive_and_trimmed()
    {
        var resolver = CategoryResolver.FromMappings([(Sku, "LAP-12", "Laptops")]);
        resolver.Resolve(productSku: "  lap-12  ", productName: null).Should().Be("Laptops");
    }

    // (d) Nothing matches → null. The transaction stays uncategorized (still processable).
    [Fact]
    public void No_match_returns_null()
    {
        var resolver = CategoryResolver.FromMappings([(Sku, "LAP-12", "Laptops")]);
        resolver.Resolve(productSku: "DELL-pol", productName: "Dell server").Should().BeNull();
        CategoryResolver.Empty.Resolve("anything", "anything").Should().BeNull();
    }

    // (d cont.) A transaction ingests fine with a null category — enrichment never blocks a real sale.
    [Fact]
    public void Ingesting_with_a_null_category_does_not_throw()
    {
        var act = () => CompensationTransaction.Ingest(
            Guid.NewGuid(), "REF-1", Guid.NewGuid(), Money.Of(100m, "EUR"),
            new DateOnly(2026, 7, 1), TransactionSource.Manual, "tester",
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), category: null);

        act.Should().NotThrow();
        act().Category.Should().BeNull();
    }

    // A blank category normalizes to null (same rule as the other descriptive fields), never an error.
    [Fact]
    public void Blank_category_normalizes_to_null()
    {
        var tx = CompensationTransaction.Ingest(
            Guid.NewGuid(), "REF-2", Guid.NewGuid(), Money.Of(100m, "EUR"),
            new DateOnly(2026, 7, 1), TransactionSource.Manual, "tester",
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), category: "   ");

        tx.Category.Should().BeNull();
    }
}
