using System.Text.Json;
using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.UnitTests.Domain;

/// <summary>
/// ★★ THE NET UNDER THE VALIDATION WORK ITEM, WRITTEN BEFORE IT TOUCHED ANYTHING.
///
/// Adding invariants to <see cref="RateTable"/>'s factories is a WRITE-side change. Every table
/// already sitting in PlanRules or frozen inside a credit's RuleSnapshot must keep deserialising
/// EXACTLY as it does today — malformed or not. A rule saved in June whose tiers stop at 7500 is
/// still the rule somebody was paid under; refusing to read it back would not fix that payment, it
/// would hide it.
///
/// ★ THE PAYLOADS BELOW ARE VERBATIM FROM THE DATABASE. They are the 8 distinct RateTable JSON
/// shapes behind the 15 real tiered/attainment rules (4 Tiered rules to 3 shapes, 11 attainment
/// rules to 5 shapes), plus a real RuleSnapshot. Several of them violate invariants this work item
/// introduces — that is the point of the file.
///
/// ★ IF A TEST HERE GOES RED, THE WORK ITEM BROKE READING. Do not adjust an expectation.
/// </summary>
public sealed class RateTableReadCharacterizationTests
{
    private static readonly JsonSerializerOptions PersistedOptions = BuildPersistedOptions();

    // Mirrors PlanRuleConfiguration.cs:19-25 and RuleSnapshotJsonConverter.cs:13-18 — the two
    // option sets that read persisted rules. Both are Web defaults plus MoneyJsonConverter.
    private static JsonSerializerOptions BuildPersistedOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        return opts;
    }

    // ── The 3 distinct Tiered shapes behind the 4 real Tiered rules ───────────────────────────

    // ENKIO Plan 2026 (x2 rules). Tiers expressed as RATIOS inside a table the engine walks over
    // AMOUNTS, with gaps between every pair (0.7999 -> 0.8) and a bounded last tier.
    private const string TieredEnkio =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":0.7999,"rate":0.075},{"from":0.8,"to":1,"rate":0.1},{"from":1.0001,"to":1.25,"rate":0.125},{"from":1.2501,"to":99.99,"rate":0.15}],"attainmentTiers":null}""";

    // Pounds 2 Rules / "RL-1". Contiguous, but the last tier is bounded at 10000.
    private const string TieredRl1 =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":1000,"rate":0.05},{"from":1000,"to":5000,"rate":0.08},{"from":5000,"to":10000,"rate":0.09}],"attainmentTiers":null}""";

    // Q3 2026 - Plan HubSpot E2E / "Acelerador Laptops". Carries splitAtQuota on a Tiered table.
    private const string TieredLaptops =
        """{"_schema":1,"type":1,"flatRate":null,"tiers":[{"from":0,"to":10000,"rate":0.04},{"from":10000,"to":25000,"rate":0.06},{"from":25000,"to":100000,"rate":0.08}],"attainmentTiers":null,"splitAtQuota":false}""";

    // ── The 5 distinct attainment shapes behind the 11 real attainment rules ──────────────────

    // EU Accelerator Q2 2026. ★ THE REFERENCE SHAPE: ratios, last tier open.
    private const string AttReference =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":1,"rate":0.04},{"attainmentFrom":1,"attainmentTo":null,"rate":0.07}],"splitAtQuota":true}""";

    // "Acelerador Hardware Premium" (x3 rules). Boundaries in absolute currency, last tier bounded.
    private const string AttAbsolute20k =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":20000,"rate":0.04},{"attainmentFrom":20000,"attainmentTo":50000,"rate":0.06},{"attainmentFrom":50000,"attainmentTo":100000,"rate":0.08}],"splitAtQuota":false}""";

    // "Claude Code - Rule #1" (x4 rules). Same defect, different numbers.
    private const string AttAbsolute2500A =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":2500,"rate":0.05},{"attainmentFrom":2500,"attainmentTo":5000,"rate":0.06},{"attainmentFrom":5000,"attainmentTo":7500,"rate":0.08}],"splitAtQuota":false}""";

    // "CC Repro Rule". Same boundaries, different rates.
    private const string AttAbsolute2500B =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":2500,"rate":0.05},{"attainmentFrom":2500,"attainmentTo":5000,"rate":0.07},{"attainmentFrom":5000,"attainmentTo":7500,"rate":0.09}],"splitAtQuota":false}""";

    // "Q1 - (Exaggerated)". THREE tiers, ALL open — they overlap, and on the split path each one
    // charges its rate over its own overlap.
    private const string AttAllOpen =
        """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":null,"rate":0.05},{"attainmentFrom":1,"attainmentTo":null,"rate":0.08},{"attainmentFrom":2,"attainmentTo":null,"rate":0.09}],"splitAtQuota":true}""";

    public static TheoryData<string, string> AllRealTables() => new()
    {
        { "Tiered/ENKIO (ratios + gaps + bounded last)", TieredEnkio },
        { "Tiered/RL-1 (bounded last)",                  TieredRl1 },
        { "Tiered/Acelerador Laptops (bounded last)",    TieredLaptops },
        { "Attainment/reference (ratios, open last)",    AttReference },
        { "Attainment/absolute 20k (bounded last)",      AttAbsolute20k },
        { "Attainment/absolute 2500-A (bounded last)",   AttAbsolute2500A },
        { "Attainment/absolute 2500-B (bounded last)",   AttAbsolute2500B },
        { "Attainment/all tiers open (overlapping)",     AttAllOpen },
    };

    [Theory]
    [MemberData(nameof(AllRealTables))]
    public void Every_real_rate_table_in_the_database_still_deserialises(string label, string json)
    {
        var act = () => JsonSerializer.Deserialize<RateTable>(json, PersistedOptions);

        act.Should().NotThrow($"'{label}' is a row in PlanRules and must stay readable");
        act()!.Should().NotBeNull();
    }

    [Fact]
    public void A_malformed_attainment_table_round_trips_with_every_tier_intact()
    {
        var table = JsonSerializer.Deserialize<RateTable>(AttAbsolute2500A, PersistedOptions)!;

        table.Type.Should().Be(RateTableType.AttainmentBased);
        table.SplitAtQuota.Should().BeFalse();
        table.AttainmentTiers.Should().HaveCount(3);
        table.AttainmentTiers![2].AttainmentFrom.Should().Be(5000m);
        table.AttainmentTiers![2].AttainmentTo.Should().Be(7500m);
        table.AttainmentTiers![2].Rate.Should().Be(0.08m);
    }

    [Fact]
    public void A_tiered_table_with_gaps_round_trips_with_the_gaps_preserved()
    {
        var table = JsonSerializer.Deserialize<RateTable>(TieredEnkio, PersistedOptions)!;

        table.Tiers.Should().HaveCount(4);
        table.Tiers![0].To.Should().Be(0.7999m);
        table.Tiers![1].From.Should().Be(
            0.8m, "the gap between 0.7999 and 0.8 is stored data, not a defect to repair on read");
        table.Tiers![3].To.Should().Be(99.99m);
    }

    [Fact]
    public void An_attainment_table_missing_splitAtQuota_reads_as_bracket_lookup()
    {
        // 29 credits were written before the property existed. Absent => false => bracket lookup,
        // which is what the engine computed for them.
        const string legacy =
            """{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":1,"rate":0.04},{"attainmentFrom":1,"attainmentTo":null,"rate":0.07}]}""";

        JsonSerializer.Deserialize<RateTable>(legacy, PersistedOptions)!.SplitAtQuota.Should().BeFalse();
    }

    /// <summary>
    /// A verbatim RuleSnapshot from credit ABBD942B-F03A-4BAF-BCCE-4D60715BF664, whose rate table
    /// violates the "last tier open" invariant. The snapshot is how a paid credit explains itself;
    /// it has to keep opening.
    /// </summary>
    [Fact]
    public void A_real_credits_RuleSnapshot_with_a_malformed_table_still_deserialises()
    {
        const string snapshotJson =
            """{"ruleId":"62e4d078-a64a-4a60-9473-ffc67257fb6b","planId":"5f423025-d4c1-4f95-a16b-e68eb8175168","planVersion":1,"ruleName":"Claude Code - Rule #1","rateTable":{"_schema":1,"type":2,"flatRate":null,"tiers":null,"attainmentTiers":[{"attainmentFrom":0,"attainmentTo":2500,"rate":0.05},{"attainmentFrom":2500,"attainmentTo":5000,"rate":0.06},{"attainmentFrom":5000,"attainmentTo":7500,"rate":0.08}],"splitAtQuota":false},"trigger":{"_schema":1,"logicalOperator":0,"conditions":[]},"measurement":{"_schema":1,"type":0,"sourceField":"amount","aggregation":0},"frozenAt":"2026-07-22T11:08:56.8226932+00:00"}""";

        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        opts.Converters.Add(new RuleSnapshotJsonConverter());

        var snapshot = JsonSerializer.Deserialize<RuleSnapshot>(snapshotJson, opts)!;

        snapshot.RuleName.Should().Be("Claude Code - Rule #1");
        snapshot.PlanVersion.Should().Be(1);
        snapshot.RateTable.AttainmentTiers.Should().HaveCount(3);
        snapshot.RateTable.AttainmentTiers![2].AttainmentTo.Should().Be(7500m);
    }

    // ── Border convention: inclusive on BOTH ends, and the upper tier wins the shared point ────
    //
    // CommissionCalculator.cs:217-220 selects with `From <= x && x <= To` and LastOrDefault, so on
    // the reference table x = 1 matches BOTH tiers and the second one wins. Any "no overlap" rule
    // written as `To >= next.From` would reject the one table in the database that is correct.

    [Theory]
    [InlineData(0.0,    0.04)]   // bottom edge of tier 1, inclusive
    [InlineData(0.5,    0.04)]   // inside tier 1
    [InlineData(1.0,    0.07)]   // SHARED edge — the upper tier wins
    [InlineData(1.0001, 0.07)]   // inside the open tier
    [InlineData(9.1009, 0.07)]   // the highest attainment observed in real data
    public void The_reference_table_resolves_its_shared_boundary_in_favour_of_the_upper_tier(
        double attainment, double expectedRate)
    {
        var table = JsonSerializer.Deserialize<RateTable>(AttReference, PersistedOptions)!;

        var commission = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(10_000m, "EUR"), table.AttainmentTiers!, (decimal)attainment);

        commission.Amount.Should().Be(10_000m * (decimal)expectedRate);
    }

    [Fact]
    public void The_reference_table_touches_at_its_boundary_and_therefore_has_no_gap()
    {
        // The formulation this work item adopts for BOTH "no overlap" and "no gaps" is a single
        // equality: tiers[i].To == tiers[i+1].From. This asserts the reference table satisfies it,
        // which is the check the work item asks for before the invariant is written.
        var table = JsonSerializer.Deserialize<RateTable>(AttReference, PersistedOptions)!;

        table.AttainmentTiers![0].AttainmentTo.Should().Be(table.AttainmentTiers![1].AttainmentFrom);
        table.AttainmentTiers![^1].AttainmentTo.Should().BeNull();
    }
}
