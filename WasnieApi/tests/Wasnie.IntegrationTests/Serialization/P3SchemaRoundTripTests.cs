using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.IntegrationTests.Serialization;

/// <summary>
/// WI-CALC-A.0 integration tests: verifies that the new nullable schema columns
/// (PlanPeriodType, Rule.EffectivePeriod, Rule.Tag) round-trip correctly through EF Core.
/// Uses an isolated Testcontainers MSSQL instance so this fixture does not interfere
/// with the shared integration test collection.
/// </summary>
[Collection(P3SchemaRoundTripCollection.Name)]
public sealed class P3SchemaRoundTripTests(P3SchemaRoundTripFixture fixture)
{
    // ── PlanPeriodType round-trip ────────────────────────────────────────────

    [Fact]
    public async Task Plan_WithPeriodType_RoundTripsThroughDb()
    {
        var planId = Guid.NewGuid();
        var tenantId = Guid.Empty;

        await using (var db = fixture.CreateDb())
        {
            var plan = Plan.Create(
                tenantId, "Monthly Sales Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "PLN", "test-user", planId,
                DateTimeOffset.UtcNow, Guid.NewGuid(),
                periodType: PlanPeriodType.Monthly);

            db.Set<Plan>().Add(plan);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDb())
        {
            var loaded = await db.Set<Plan>()
                .AsNoTracking()
                .FirstAsync(p => p.Id == planId);

            loaded.PeriodType.Should().Be(PlanPeriodType.Monthly);
        }
    }

    [Fact]
    public async Task Plan_WithNullPeriodType_RoundTripsThroughDb()
    {
        var planId = Guid.NewGuid();
        var tenantId = Guid.Empty;

        await using (var db = fixture.CreateDb())
        {
            var plan = Plan.Create(
                tenantId, "No PeriodType Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "EUR", "test-user", planId,
                DateTimeOffset.UtcNow, Guid.NewGuid());

            db.Set<Plan>().Add(plan);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDb())
        {
            var loaded = await db.Set<Plan>()
                .AsNoTracking()
                .FirstAsync(p => p.Id == planId);

            loaded.PeriodType.Should().BeNull();
        }
    }

    // ── Rule.Tag + Rule.EffectivePeriod round-trip ────────────────────────────

    [Fact]
    public async Task Plan_WithRuleTagAndEffectivePeriod_RoundTripsThroughDb()
    {
        var planId = Guid.NewGuid();
        var tenantId = Guid.Empty;
        var ruleStart = new DateOnly(2026, 4, 1);
        var ruleEnd = new DateOnly(2026, 6, 30);

        await using (var db = fixture.CreateDb())
        {
            var plan = Plan.Create(
                tenantId, "Promo Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "PLN", "test-user", planId,
                DateTimeOffset.UtcNow, Guid.NewGuid());

            plan.AddRule(
                "Spring Promo",
                sortOrder: 1,
                measurement: new Measurement
                {
                    Type = MeasurementType.Revenue,
                    SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum
                },
                rateTable: RateTable.Flat(0.08m),
                effectivePeriod: DateRange.Of(ruleStart, ruleEnd),
                tag: "Promo Q2 2026");

            db.Set<Plan>().Add(plan);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDb())
        {
            var loaded = await db.Set<Plan>()
                .AsNoTracking()
                .Include(p => p.Rules)
                .FirstAsync(p => p.Id == planId);

            var rule = loaded.Rules.Should().ContainSingle().Which;
            rule.Tag.Should().Be("Promo Q2 2026");
            rule.EffectivePeriod.Should().NotBeNull();
            rule.EffectivePeriod!.Start.Should().Be(ruleStart);
            rule.EffectivePeriod.End.Should().Be(ruleEnd);
        }
    }

    [Fact]
    public async Task Plan_WithRuleNullTagAndNullEffectivePeriod_RoundTripsThroughDb()
    {
        var planId = Guid.NewGuid();
        var tenantId = Guid.Empty;

        await using (var db = fixture.CreateDb())
        {
            var plan = Plan.Create(
                tenantId, "Plain Rule Plan", "desc",
                DateRange.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                "EUR", "test-user", planId,
                DateTimeOffset.UtcNow, Guid.NewGuid());

            plan.AddRule(
                "Base Commission",
                sortOrder: 1,
                measurement: new Measurement
                {
                    Type = MeasurementType.Revenue,
                    SourceField = "amount",
                    Aggregation = MeasurementAggregation.Sum
                },
                rateTable: RateTable.Flat(0.05m));

            db.Set<Plan>().Add(plan);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDb())
        {
            var loaded = await db.Set<Plan>()
                .AsNoTracking()
                .Include(p => p.Rules)
                .FirstAsync(p => p.Id == planId);

            var rule = loaded.Rules.Should().ContainSingle().Which;
            rule.Tag.Should().BeNull();
            rule.EffectivePeriod.Should().BeNull();
        }
    }
}
