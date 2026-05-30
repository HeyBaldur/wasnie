using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.IntegrationTests.Serialization;

[Collection(MoneyRoundTripCollection.Name)]
public sealed class MoneyRoundTripTests(MoneyRoundTripFixture fixture)
{
    private const string TriggerJson = @"{""_schema"":1,""logicalOperator"":0,""conditions"":[]}";
    private const string MeasurementJson = @"{""_schema"":1,""type"":0,""sourceField"":""amount"",""aggregation"":0}";
    private const string RateTableJson = @"{""_schema"":1,""type"":0,""flatRate"":0.05}";

    // ── Cap.Amount round-trip (PlanRuleConfiguration → MoneyJsonConverter) ────

    [Fact]
    public async Task PersistRuleWithCap_ReloadedCap_HasCorrectMoneyAmount()
    {
        var planId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var capJson = @"{""_schema"":1,""amount"":{""amount"":1500.25,""currency"":""EUR""},""scope"":0}";

        await using var db = fixture.CreateDb();
        await SeedPlanAsync(db, planId);
        await InsertRuleAsync(db, ruleId, planId, "Cap Test Rule", capJson, floorJson: null);
        db.ChangeTracker.Clear();

        var rule = await db.Set<Rule>().AsNoTracking().FirstAsync(r => r.Id == ruleId);

        rule.Cap.Should().NotBeNull();
        rule.Cap!.Amount.Should().Be(Money.Of(1500.25m, "EUR"));
        rule.Cap.Amount.Currency.Should().Be("EUR");
        rule.Floor.Should().BeNull();
    }

    [Fact]
    public async Task PersistRuleWithFloor_ReloadedFloor_HasCorrectMoneyAmount()
    {
        var planId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var floorJson = @"{""_schema"":1,""amount"":{""amount"":250.1234,""currency"":""USD""}}";

        await using var db = fixture.CreateDb();
        await SeedPlanAsync(db, planId);
        await InsertRuleAsync(db, ruleId, planId, "Floor Test Rule", capJson: null, floorJson);
        db.ChangeTracker.Clear();

        var rule = await db.Set<Rule>().AsNoTracking().FirstAsync(r => r.Id == ruleId);

        rule.Floor.Should().NotBeNull();
        rule.Floor!.Amount.Should().Be(Money.Of(250.1234m, "USD"));
        rule.Floor.Amount.Currency.Should().Be("USD");
        rule.Cap.Should().BeNull();
    }

    [Fact]
    public async Task PersistRuleWithCap_BackwardCompatStoredFormat_DeserializesCorrectly()
    {
        // JSON written by the old [JsonConstructor] path — verifies no data loss.
        var planId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var capJson = @"{""_schema"":1,""amount"":{""amount"":9999.9999,""currency"":""PLN""},""scope"":2}";

        await using var db = fixture.CreateDb();
        await SeedPlanAsync(db, planId);
        await InsertRuleAsync(db, ruleId, planId, "BackwardCompat Rule", capJson, floorJson: null);
        db.ChangeTracker.Clear();

        var rule = await db.Set<Rule>().AsNoTracking().FirstAsync(r => r.Id == ruleId);

        rule.Cap.Should().NotBeNull();
        rule.Cap!.Amount.Amount.Should().Be(9999.9999m);
        rule.Cap.Amount.Currency.Should().Be("PLN");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task SeedPlanAsync(Microsoft.EntityFrameworkCore.DbContext db, Guid planId)
    {
        var tenantId = Guid.Empty;
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO CompensationPlans
                (Id, TenantId, Name, Description, Currency, Status, Version,
                 EffectiveStart, EffectiveEnd, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            VALUES
                ({planId}, {tenantId}, 'Seed Plan', '', 'EUR', 'Draft', 1,
                 '2025-01-01', '2025-12-31',
                 '2025-01-01T00:00:00+00:00', 'test', '2025-01-01T00:00:00+00:00', 'test')
            """);
    }

    private async Task InsertRuleAsync(
        Microsoft.EntityFrameworkCore.DbContext db,
        Guid ruleId,
        Guid planId,
        string name,
        string? capJson,
        string? floorJson)
    {
        // Use ExecuteSqlAsync (FormattableString) so JSON values are passed as SQL parameters,
        // not embedded in the SQL string (which would cause {}-as-placeholder parsing errors).
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO PlanRules
                (Id, PlanId, Name, SortOrder, IsActive, [Trigger], Measurement, RateTable, Cap, Floor)
            VALUES
                ({ruleId}, {planId}, {name}, 1, 1,
                 {TriggerJson}, {MeasurementJson}, {RateTableJson},
                 {capJson}, {floorJson})
            """);
    }
}
