using FluentAssertions;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Features.Subscription;
using Wasnie.UnitTests.TestDoubles;

namespace Wasnie.UnitTests.Services;

/// <summary>
/// KAN-77 — which Stripe product is which plan. Replaces the old product → tier mapping tests: plans are codes
/// from configuration, and a legacy product someone still pays on must never resolve to "nothing".
/// </summary>
public sealed class SubscriptionPlanCatalogTests
{
    private static readonly SubscriptionPlanDefinition Pro = new() { Code = "pro" };
    private static readonly SubscriptionPlanDefinition Team = new()
    {
        Code = "team", StripeProductIds = ["prod_team"], MaxPayees = 50, MaxPlans = 10,
    };

    private static Dictionary<string, string> Meta(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);

    [Fact]
    public void Metadata_plan_wins()
    {
        var catalog = TestPlanCatalog.Create(Pro, Team);

        catalog.ResolveStripeProduct("prod_team", Meta(("plan", "pro")))!.Code
            .Should().Be("pro", "the product states its plan; the config list is only for products that do not");
    }

    [Fact]
    public void Metadata_naming_an_unknown_plan_resolves_to_nothing()
    {
        TestPlanCatalog.Create(Pro).ResolveStripeProduct("prod_x", Meta(("plan", "enterprise"))).Should().BeNull();
    }

    [Fact]
    public void A_product_listed_in_the_catalog_resolves_without_metadata()
    {
        TestPlanCatalog.Create(Pro, Team).ResolveStripeProduct("prod_team", Meta())!.Code.Should().Be("team");
    }

    [Theory]
    [InlineData("starter")]
    [InlineData("growth")]
    [InlineData("scale")]
    public void A_legacy_tier_product_is_honoured_as_the_default_plan(string legacyTier)
    {
        // ★★ "Whoever pays is not interrupted": a customer still on an old Starter/Growth/Scale price keeps a
        // plan. On 2026-09-14 the €299 product itself still carried metadata.tier = starter.
        TestPlanCatalog.Create(Pro, Team).ResolveStripeProduct("prod_old", Meta(("tier", legacyTier)))!.Code
            .Should().Be("pro");
    }

    [Fact]
    public void An_unknown_product_resolves_to_nothing()
    {
        TestPlanCatalog.Create(Pro).ResolveStripeProduct("prod_unknown", Meta()).Should().BeNull();
    }

    [Fact]
    public void More_room_is_an_upgrade_and_unlimited_is_the_most_room()
    {
        var catalog = TestPlanCatalog.Create(Pro, Team);

        catalog.IsUpgrade(Team, Pro).Should().BeTrue("pro is unlimited");
        catalog.IsUpgrade(Pro, Team).Should().BeFalse();
        catalog.IsUpgrade(null, Team).Should().BeTrue("an unknown origin counts as an upgrade");
    }

    [Fact]
    public void The_shipped_configuration_is_valid_and_a_broken_one_is_not()
    {
        SubscriptionPlanCatalog.Validate(new BillingOptions { DefaultPlanCode = "pro", Plans = [Pro] })
            .Should().BeEmpty();

        SubscriptionPlanCatalog.Validate(new BillingOptions { DefaultPlanCode = "pro", Plans = [] })
            .Should().NotBeEmpty("no plans at all");
        SubscriptionPlanCatalog.Validate(new BillingOptions { DefaultPlanCode = "gold", Plans = [Pro] })
            .Should().NotBeEmpty("the default plan must exist");
        SubscriptionPlanCatalog.Validate(new BillingOptions
            { DefaultPlanCode = "pro", Plans = [Pro, new SubscriptionPlanDefinition { Code = "PRO" }] })
            .Should().NotBeEmpty("codes are unique, case-insensitively");
    }
}
