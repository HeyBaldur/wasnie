using FluentAssertions;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Exceptions;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// The six invariants, exercised on BOTH factories, plus the door the HTTP commands now come
/// through.
///
/// ★ EVERY CASE ASSERTS THE SPECIFIC MESSAGE, NOT JUST THE EXCEPTION TYPE. Six ways to build a
/// broken ladder that all report "invalid rate table" is a form that tells the user to guess. The
/// tests pin the wording so the distinction cannot quietly collapse back into one generic error.
/// </summary>
public sealed class RateTableInvariantTests
{
    private static RateTier T(decimal from, decimal? to, decimal rate) => new() { From = from, To = to, Rate = rate };

    private static AttainmentTier A(decimal from, decimal? to, decimal rate) =>
        new() { AttainmentFrom = from, AttainmentTo = to, Rate = rate };

    // ── Invariant 1 — non-empty ───────────────────────────────────────────────────────────────

    [Fact]
    public void Tiered_rejects_an_empty_ladder()
        => FluentActions.Invoking(() => RateTable.Tiered([]))
            .Should().Throw<DomainException>()
            .WithMessage("Tiered rate table must have at least one tier.");

    [Fact]
    public void Attainment_rejects_an_empty_ladder()
        => FluentActions.Invoking(() => RateTable.AttainmentBased([]))
            .Should().Throw<DomainException>()
            .WithMessage("Attainment-based rate table must have at least one tier.");

    // ── Invariant 2 — the last tier must be open ──────────────────────────────────────────────
    //
    // The one that was missing from both factories, and the one that silently paid zero to anyone
    // above the top bracket. Eight real attainment tables violate it.

    [Fact]
    public void Tiered_rejects_a_bounded_last_tier()
        => FluentActions.Invoking(() => RateTable.Tiered([T(0m, 1000m, 0.05m), T(1000m, 10000m, 0.09m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*last tier must be open-ended*amount above every tier*ends at 10000*");

    [Fact]
    public void Attainment_rejects_a_bounded_last_tier()
        => FluentActions.Invoking(() => RateTable.AttainmentBased([A(0m, 2500m, 0.05m), A(2500m, 7500m, 0.08m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*last tier must be open-ended*attainment ratio above every tier*ends at 7500*");

    // ── Invariant 3 — every tier before the last is closed ────────────────────────────────────

    [Fact]
    public void Tiered_rejects_an_open_tier_that_is_not_the_last()
        => FluentActions.Invoking(() => RateTable.Tiered([T(0m, null, 0.05m), T(1000m, null, 0.09m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*tier 1 must have an upper bound because it is not the last tier*");

    /// <summary>
    /// The real shape of "Q1 - (Exaggerated)": three tiers, all open. On the split path each one
    /// charges its rate over its own overlap, so a 200.000 sale against a 50.000 quota earns 15,5%
    /// from a table whose highest declared rate is 9%.
    /// </summary>
    [Fact]
    public void Attainment_rejects_the_all_tiers_open_shape_that_stacks_rates()
        => FluentActions.Invoking(() => RateTable.AttainmentBased(
                [A(0m, null, 0.05m), A(1m, null, 0.08m), A(2m, null, 0.09m)], splitAtQuota: true))
            .Should().Throw<DomainException>()
            .WithMessage("*tier 1 must have an upper bound because it is not the last tier*");

    // ── Invariant 4 — strictly ascending ──────────────────────────────────────────────────────

    [Fact]
    public void Tiered_rejects_tiers_out_of_order()
        => FluentActions.Invoking(() => RateTable.Tiered([T(5000m, 10000m, 0.09m), T(0m, null, 0.05m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*must be ordered ascending*tier 1 starts at 5000*tier 2 starts at 0*");

    [Fact]
    public void Attainment_rejects_two_tiers_starting_at_the_same_point()
        => FluentActions.Invoking(() => RateTable.AttainmentBased([A(1m, 1m, 0.04m), A(1m, null, 0.07m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*must be ordered ascending*");

    // ── Invariant 5 — no overlap ──────────────────────────────────────────────────────────────

    [Fact]
    public void Tiered_rejects_overlapping_tiers()
        => FluentActions.Invoking(() => RateTable.Tiered([T(0m, 100m, 0.05m), T(80m, null, 0.08m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*tiers 1 and 2 overlap*ends at 100*starts at 80*");

    [Fact]
    public void Attainment_rejects_overlapping_tiers()
        => FluentActions.Invoking(() => RateTable.AttainmentBased([A(0m, 1.2m, 0.04m), A(1m, null, 0.07m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*tiers 1 and 2 overlap*ends at 1.2*starts at 1*");

    // ── Invariant 6 — no gaps ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ENKIO shape: 0–0.7999 then 0.8–1. The engine walks tier WIDTHS, so a declared gap does
    /// not skip anything — it shifts where every rate above it starts.
    /// </summary>
    [Fact]
    public void Tiered_rejects_a_gap_between_tiers()
        => FluentActions.Invoking(() => RateTable.Tiered([T(0m, 0.7999m, 0.075m), T(0.8m, null, 0.1m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*tiers 1 and 2 leave a gap*ends at 0.7999*starts at 0.8*earn no rate at all*");

    [Fact]
    public void Attainment_rejects_a_gap_between_tiers()
        => FluentActions.Invoking(() => RateTable.AttainmentBased([A(0m, 0.99m, 0.04m), A(1m, null, 0.07m)]))
            .Should().Throw<DomainException>()
            .WithMessage("*tiers 1 and 2 leave a gap*attainment ratio in between*");

    // ── The shape that must keep being accepted ───────────────────────────────────────────────

    [Fact]
    public void The_reference_table_is_accepted_unchanged()
    {
        var table = RateTable.AttainmentBased([A(0m, 1m, 0.04m), A(1m, null, 0.07m)], splitAtQuota: true);

        table.Type.Should().Be(RateTableType.AttainmentBased);
        table.SplitAtQuota.Should().BeTrue();
        table.AttainmentTiers.Should().HaveCount(2);
    }

    [Fact]
    public void A_single_open_tier_is_accepted()
    {
        RateTable.Tiered([T(0m, null, 0.05m)]).Tiers.Should().HaveCount(1);
        RateTable.AttainmentBased([A(0m, null, 0.05m)]).AttainmentTiers.Should().HaveCount(1);
    }

    // ── The command's door: RateTableRequest.ToDomain ──────────────────────────────────────────

    [Fact]
    public void The_request_type_routes_a_malformed_attainment_table_into_the_factory()
    {
        var request = new RateTableRequest(
            RateTableType.AttainmentBased,
            FlatRate: null,
            Tiers: null,
            AttainmentTiers:
            [
                new AttainmentTierRequest(0m, 20000m, 0.04m),
                new AttainmentTierRequest(20000m, 50000m, 0.06m),
                new AttainmentTierRequest(50000m, 100000m, 0.08m),
            ]);

        FluentActions.Invoking(() => request.ToDomain())
            .Should().Throw<DomainException>()
            .WithMessage("*last tier must be open-ended*");
    }

    [Fact]
    public void The_request_type_builds_a_well_formed_table_and_keeps_its_flags()
    {
        var request = new RateTableRequest(
            RateTableType.AttainmentBased,
            FlatRate: null,
            Tiers: null,
            AttainmentTiers: [new AttainmentTierRequest(0m, 1m, 0.04m), new AttainmentTierRequest(1m, null, 0.07m)],
            SplitAtQuota: true);

        var table = request.ToDomain();

        table.AttainmentTiers.Should().HaveCount(2);
        table.AttainmentTiers![1].AttainmentTo.Should().BeNull();
        table.SplitAtQuota.Should().BeTrue();
    }

    [Fact]
    public void The_request_type_passes_a_flat_rate_straight_through()
        => new RateTableRequest(RateTableType.Flat, 0.05m, null, null).ToDomain().FlatRate.Should().Be(0.05m);

    [Fact]
    public void The_request_type_rejects_a_flat_table_with_no_rate()
        => FluentActions.Invoking(() => new RateTableRequest(RateTableType.Flat, null, null, null).ToDomain())
            .Should().Throw<DomainException>()
            .WithMessage("A flat rate table requires a rate.");
}
