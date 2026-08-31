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
/// ★ EVERY CASE ASSERTS THE CODE AND ITS PARAMETERS, NOT A SENTENCE. These used to pin English
/// wording, which was the right instinct against a generic "invalid rate table" but the wrong
/// contract: the wording now lives in the front end's EN/ES/PL files and can be rewritten there
/// without touching the engine. What must not drift is the CODE — the front end matches it against
/// its whitelist — and the PARAMETERS, because a sentence that names the wrong tier is worse than a
/// vague one.
/// </summary>
public sealed class RateTableInvariantTests
{
    private static RateTier T(decimal from, decimal? to, decimal rate) => new() { From = from, To = to, Rate = rate };

    private static AttainmentTier A(decimal from, decimal? to, decimal rate) =>
        new() { AttainmentFrom = from, AttainmentTo = to, Rate = rate };

    /// <summary>The coded refusal an action threw, or a failed assertion naming what it threw instead.</summary>
    private static DomainCodedException Refusal(Action act)
        => FluentActions.Invoking(act).Should().Throw<DomainCodedException>().Which;

    // ── Invariant 1 — non-empty ───────────────────────────────────────────────────────────────

    [Fact]
    public void Tiered_rejects_an_empty_ladder()
        => Refusal(() => RateTable.Tiered([])).Code.Should().Be(RateTableInvariant.Empty);

    [Fact]
    public void Attainment_rejects_an_empty_ladder()
        => Refusal(() => RateTable.AttainmentBased([])).Code.Should().Be(RateTableInvariant.Empty);

    // ── Invariant 2 — the last tier must be open ──────────────────────────────────────────────
    //
    // The one that was missing from both factories, and the one that silently zeroed overachievers.
    // Eight real attainment tables violate it.

    [Fact]
    public void Tiered_rejects_a_bounded_last_tier()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(0m, 1000m, 0.05m), T(1000m, 10000m, 0.09m)]));

        refusal.Code.Should().Be(RateTableInvariant.LastTierMustBeOpen);
        refusal.Parameters["tierNumber"].Should().Be(2);
        refusal.Parameters["endsAt"].Should().Be(10000m);
        refusal.Parameters["bound"].Should().Be(RateTableBound.Amount);
    }

    [Fact]
    public void Attainment_rejects_a_bounded_last_tier()
    {
        var refusal = Refusal(() => RateTable.AttainmentBased([A(0m, 2500m, 0.05m), A(2500m, 7500m, 0.08m)]));

        refusal.Code.Should().Be(RateTableInvariant.LastTierMustBeOpen);
        refusal.Parameters["tierNumber"].Should().Be(2);
        refusal.Parameters["endsAt"].Should().Be(7500m);
        refusal.Parameters["bound"].Should().Be(RateTableBound.AttainmentRatio);
    }

    // ── Invariant 3 — every tier before the last is closed ────────────────────────────────────

    [Fact]
    public void Tiered_rejects_an_open_tier_that_is_not_the_last()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(0m, null, 0.05m), T(1000m, null, 0.09m)]));

        refusal.Code.Should().Be(RateTableInvariant.NonLastTierMustBeClosed);
        refusal.Parameters["tierNumber"].Should().Be(1);
    }

    /// <summary>
    /// The real shape of "Q1 - (Exaggerated)": three tiers, all open. On the split path each one
    /// charges its rate over its own overlap, so a 200.000 sale against a 50.000 quota earns 15,5%
    /// from a table whose highest declared rate is 9%.
    /// </summary>
    [Fact]
    public void Attainment_rejects_the_all_tiers_open_shape_that_stacks_rates()
    {
        var refusal = Refusal(() => RateTable.AttainmentBased(
            [A(0m, null, 0.05m), A(1m, null, 0.08m), A(2m, null, 0.09m)], splitAtQuota: true));

        refusal.Code.Should().Be(RateTableInvariant.NonLastTierMustBeClosed);
        refusal.Parameters["tierNumber"].Should().Be(1);
    }

    // ── Invariant 4 — strictly ascending ──────────────────────────────────────────────────────

    [Fact]
    public void Tiered_rejects_tiers_out_of_order()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(5000m, 10000m, 0.09m), T(0m, null, 0.05m)]));

        refusal.Code.Should().Be(RateTableInvariant.TiersOutOfOrder);
        refusal.Parameters["tierNumber"].Should().Be(1);
        refusal.Parameters["nextTierNumber"].Should().Be(2);
        refusal.Parameters["startsAt"].Should().Be(5000m);
        refusal.Parameters["nextStartsAt"].Should().Be(0m);
    }

    [Fact]
    public void Attainment_rejects_two_tiers_starting_at_the_same_point()
        => Refusal(() => RateTable.AttainmentBased([A(1m, 1m, 0.04m), A(1m, null, 0.07m)]))
            .Code.Should().Be(RateTableInvariant.TiersOutOfOrder);

    // ── Invariant 5 — no overlap ──────────────────────────────────────────────────────────────

    [Fact]
    public void Tiered_rejects_overlapping_tiers()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(0m, 100m, 0.05m), T(80m, null, 0.08m)]));

        refusal.Code.Should().Be(RateTableInvariant.TiersOverlap);
        refusal.Parameters["tierNumber"].Should().Be(1);
        refusal.Parameters["nextTierNumber"].Should().Be(2);
        refusal.Parameters["endsAt"].Should().Be(100m);
        refusal.Parameters["nextStartsAt"].Should().Be(80m);
    }

    [Fact]
    public void Attainment_rejects_overlapping_tiers()
    {
        var refusal = Refusal(() => RateTable.AttainmentBased([A(0m, 1.2m, 0.04m), A(1m, null, 0.07m)]));

        refusal.Code.Should().Be(RateTableInvariant.TiersOverlap);
        refusal.Parameters["endsAt"].Should().Be(1.2m);
        refusal.Parameters["nextStartsAt"].Should().Be(1m);
    }

    // ── Invariant 6 — no gaps ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ENKIO shape: 0–0.7999 then 0.8–1. The engine walks tier WIDTHS, so a declared gap does
    /// not skip anything — it shifts where every rate above it starts.
    /// </summary>
    [Fact]
    public void Tiered_rejects_a_gap_between_tiers()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(0m, 0.7999m, 0.075m), T(0.8m, null, 0.1m)]));

        refusal.Code.Should().Be(RateTableInvariant.TiersLeaveGap);
        refusal.Parameters["tierNumber"].Should().Be(1);
        refusal.Parameters["nextTierNumber"].Should().Be(2);
        refusal.Parameters["endsAt"].Should().Be(0.7999m);
        refusal.Parameters["nextStartsAt"].Should().Be(0.8m);
        refusal.Parameters["bound"].Should().Be(RateTableBound.Amount);
    }

    [Fact]
    public void Attainment_rejects_a_gap_between_tiers()
    {
        var refusal = Refusal(() => RateTable.AttainmentBased([A(0m, 0.99m, 0.04m), A(1m, null, 0.07m)]));

        refusal.Code.Should().Be(RateTableInvariant.TiersLeaveGap);
        refusal.Parameters["bound"].Should().Be(RateTableBound.AttainmentRatio);
    }

    // ── ★★ Which refusal wins when a ladder breaks several rules ──────────────────────────────
    //
    // Only the first refusal is ever seen, so the order of the checks decides what the reader is
    // told. These pin the three cases where one invariant systematically implies another; there are
    // no others among the six — a bounded last tier does not imply an overlap or a gap, nor the
    // reverse, so nothing further is asserted here.

    /// <summary>
    /// ★★ THE CASE THAT WAS WRONG. A ladder typed in descending order almost always has a bounded
    /// last tier too — the tier its author thinks of as the top is sitting at the bottom of the list
    /// — and the open-last-tier check used to run first. The reader was told to open up tier 2,
    /// which was true and did not fix anything: the ladder is upside down.
    /// </summary>
    [Fact]
    public void A_descending_ladder_reports_the_order_not_the_bounded_last_tier()
    {
        var refusal = Refusal(() => RateTable.Tiered([T(1000m, 2000m, 0.09m), T(0m, 1000m, 0.05m)]));

        refusal.Code.Should().Be(RateTableInvariant.TiersOutOfOrder);
    }

    /// <summary>
    /// The same masking on the attainment side, with the real shape: an accelerator table entered
    /// top-first.
    /// </summary>
    [Fact]
    public void A_descending_attainment_ladder_reports_the_order_not_the_bounded_last_tier()
        => Refusal(() => RateTable.AttainmentBased([A(1m, 2m, 0.07m), A(0m, 1m, 0.04m)]))
            .Code.Should().Be(RateTableInvariant.TiersOutOfOrder);

    /// <summary>
    /// ★ THE SECOND MASKING PAIR. A descending ladder ALWAYS registers as an overlap as well —
    /// <c>From</c> decreases while every <c>To</c> exceeds its own <c>From</c> — so "tiers 1 and 2
    /// overlap" is available for every out-of-order ladder ever entered. Order is the more useful
    /// answer: fixing the sequence fixes the overlap, and closing the overlap of a shuffled ladder
    /// fixes nothing.
    /// </summary>
    [Fact]
    public void A_descending_ladder_with_an_open_last_tier_reports_the_order_not_the_overlap()
        => Refusal(() => RateTable.Tiered([T(5000m, 10000m, 0.09m), T(0m, null, 0.05m)]))
            .Code.Should().Be(RateTableInvariant.TiersOutOfOrder);

    /// <summary>
    /// ★ THE THIRD, AND IT IS STRUCTURAL RATHER THAN A PREFERENCE. The overlap and gap checks read a
    /// tier's upper bound, so a middle tier that has none has to be refused before they can run at
    /// all. A ladder of three open tiers reports the open middle tier, never a gap.
    /// </summary>
    [Fact]
    public void An_all_open_ladder_reports_the_unclosed_tier_not_a_gap_or_an_overlap()
        => Refusal(() => RateTable.Tiered([T(0m, null, 0.05m), T(1000m, null, 0.08m), T(2000m, null, 0.09m)]))
            .Code.Should().Be(RateTableInvariant.NonLastTierMustBeClosed);

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

        var refusal = Refusal(() => request.ToDomain());

        refusal.Code.Should().Be(RateTableInvariant.LastTierMustBeOpen);
        refusal.Parameters["tierNumber"].Should().Be(3);
        refusal.Parameters["endsAt"].Should().Be(100000m);
        refusal.Parameters["bound"].Should().Be(RateTableBound.AttainmentRatio);
    }

    /// <summary>
    /// ★ THE CODED REFUSAL IS STILL A <see cref="DomainException"/>. The handlers catch that type and
    /// turn it into a Result; only a `catch (DomainCodedException) { throw; }` placed FIRST lets this
    /// one reach the middleware with its code intact. If this inheritance ever changed, those catches
    /// would silently stop mattering and the six messages would go back to being untranslated prose.
    /// </summary>
    [Fact]
    public void A_coded_refusal_is_still_a_domain_exception()
        => FluentActions.Invoking(() => RateTable.Tiered([]))
            .Should().Throw<DomainException>();

    /// <summary>
    /// ★ AND IT CARRIES NO USER-FACING PROSE. `Message` is the code, so a caller that leaks it into a
    /// toast shows an identifier rather than an English sentence — visibly broken instead of quietly
    /// monolingual.
    /// </summary>
    [Fact]
    public void A_coded_refusal_carries_no_english_sentence()
        => Refusal(() => RateTable.Tiered([])).Message.Should().Be(RateTableInvariant.Empty);

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

    /// <summary>
    /// Not a ladder invariant, so it stays plain prose — the pass that introduced codes covered the
    /// six ladder rules and nothing else.
    /// </summary>
    [Fact]
    public void The_request_type_rejects_a_flat_table_with_no_rate()
        => FluentActions.Invoking(() => new RateTableRequest(RateTableType.Flat, null, null, null).ToDomain())
            .Should().Throw<DomainException>()
            .WithMessage("A flat rate table requires a rate.");
}
