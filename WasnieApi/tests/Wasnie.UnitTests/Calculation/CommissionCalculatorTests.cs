using FluentAssertions;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Compensation.Calculation;

namespace Wasnie.UnitTests.Calculation;

/// <summary>
/// Exhaustive unit tests for CommissionCalculator.
/// No database, no DI container — pure math in/math out.
/// Covers: Flat / Tiered / AttainmentBased rate tables, Modifier, Cap, Floor,
/// Trigger evaluation, rounding precision, currency isolation, determinism.
/// </summary>
public sealed class CommissionCalculatorTests
{
    private static readonly DateOnly TxDate = new(2026, 3, 15);
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

    private static CompensationTransaction MakeTx(
        decimal amount,
        string currency = "EUR",
        TransactionSource source = TransactionSource.Manual)
    {
        return CompensationTransaction.Ingest(
            tenantId: Guid.NewGuid(),
            referenceNumber: "REF-TEST",
            payeeId: Guid.NewGuid(),
            amount: Money.Of(amount, currency),
            transactionDate: TxDate,
            source: source,
            ingestedBy: "test",
            id: Guid.NewGuid(),
            now: Now,
            eventId: Guid.NewGuid());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FLAT rate table
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeCommission_Flat_StandardRate_ReturnsCorrectAmount()
    {
        var result = CommissionCalculator.ComputeCommission(
            Money.Of(1000m, "EUR"),
            RateTable.Flat(0.05m),
            attainmentPct: 1.0m);

        result.Amount.Should().Be(50.0000m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public void ComputeCommission_Flat_ZeroAmount_ReturnsZero()
    {
        var result = CommissionCalculator.ComputeCommission(
            Money.Of(0m, "EUR"),
            RateTable.Flat(0.10m),
            attainmentPct: 1.0m);

        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void ComputeCommission_Flat_LargeAmount_NoOverflow()
    {
        var result = CommissionCalculator.ComputeCommission(
            Money.Of(999_999m, "USD"),
            RateTable.Flat(0.07m),
            attainmentPct: 1.0m);

        result.Amount.Should().Be(69_999.9300m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void ComputeCommission_Flat_FractionalResultNormalizedToFourDecimals()
    {
        // 1 * 0.33335 = 0.33335 → 4-decimal banker's rounding: 4th digit is 3 (odd) → rounds up → 0.3334
        var result = CommissionCalculator.ComputeCommission(
            Money.Of(1m, "EUR"),
            RateTable.Flat(0.33335m),
            attainmentPct: 1.0m);

        result.Amount.Should().Be(0.3334m);
    }

    [Fact]
    public void ComputeCommission_Flat_BinaryFloatTrap_SevenPercent()
    {
        // Classic float trap: 1000 * 0.07 must be exactly 70, not 69.9999...
        var result = CommissionCalculator.ComputeCommission(
            Money.Of(1000m, "EUR"),
            RateTable.Flat(0.07m),
            attainmentPct: 1.0m);

        result.Amount.Should().Be(70.0000m);
    }

    [Fact]
    public void ComputeCommission_Flat_IgnoresAttainmentPct()
    {
        // Flat tables never use attainmentPct — passing 0 should not change result.
        var withAttainment = CommissionCalculator.ComputeCommission(
            Money.Of(500m, "EUR"), RateTable.Flat(0.10m), attainmentPct: 0.80m);
        var withoutAttainment = CommissionCalculator.ComputeCommission(
            Money.Of(500m, "EUR"), RateTable.Flat(0.10m), attainmentPct: 1.00m);

        withAttainment.Amount.Should().Be(withoutAttainment.Amount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TIERED rate table
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeTieredCommission_AmountEntirelyInFirstTier()
    {
        var tiers = new List<RateTier>
        {
            new() { From = 0m,   To = 500m,  Rate = 0.05m },
            new() { From = 500m, To = null,   Rate = 0.10m }
        };

        var result = CommissionCalculator.ComputeTieredCommission(
            Money.Of(300m, "EUR"), tiers);

        result.Amount.Should().Be(15.0000m);
    }

    [Fact]
    public void ComputeTieredCommission_ExactTierBoundary_OnlyFirstTierUsed()
    {
        var tiers = new List<RateTier>
        {
            new() { From = 0m,   To = 500m,  Rate = 0.05m },
            new() { From = 500m, To = null,   Rate = 0.10m }
        };

        var result = CommissionCalculator.ComputeTieredCommission(
            Money.Of(500m, "EUR"), tiers);

        // 500 * 5% = 25; second tier: remaining = 0 → break
        result.Amount.Should().Be(25.0000m);
    }

    [Fact]
    public void ComputeTieredCommission_CrossesBoundary_TwoTierSum()
    {
        // Classic 800-unit deal: 500@5% + 300@10% = 25 + 30 = 55
        var tiers = new List<RateTier>
        {
            new() { From = 0m,   To = 500m,  Rate = 0.05m },
            new() { From = 500m, To = null,   Rate = 0.10m }
        };

        var result = CommissionCalculator.ComputeTieredCommission(
            Money.Of(800m, "EUR"), tiers);

        result.Amount.Should().Be(55.0000m);
    }

    [Fact]
    public void ComputeTieredCommission_ThreeTiers_AllFilled()
    {
        // 1500: 0-500@5%=25, 500-1000@10%=50, 1000+@15%=75 → 150
        var tiers = new List<RateTier>
        {
            new() { From = 0m,    To = 500m,  Rate = 0.05m },
            new() { From = 500m,  To = 1000m, Rate = 0.10m },
            new() { From = 1000m, To = null,   Rate = 0.15m }
        };

        var result = CommissionCalculator.ComputeTieredCommission(
            Money.Of(1500m, "EUR"), tiers);

        result.Amount.Should().Be(150.0000m);
    }

    [Fact]
    public void ComputeTieredCommission_LastTierOpenEnded_HandlesLargeAmount()
    {
        // 5000: 0-1000@5%=50, 1000+@10%=400 → 450
        var tiers = new List<RateTier>
        {
            new() { From = 0m,    To = 1000m, Rate = 0.05m },
            new() { From = 1000m, To = null,   Rate = 0.10m }
        };

        var result = CommissionCalculator.ComputeTieredCommission(
            Money.Of(5000m, "EUR"), tiers);

        result.Amount.Should().Be(450.0000m);
    }

    [Fact]
    public void ComputeTieredCommission_ZeroAmount_ReturnsZero()
    {
        var tiers = new List<RateTier>
        {
            new() { From = 0m, To = null, Rate = 0.10m }
        };

        var result = CommissionCalculator.ComputeTieredCommission(
            Money.Of(0m, "EUR"), tiers);

        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void ComputeTieredCommission_FractionalAmountPerTier_AccumulatesExactly()
    {
        // 333@5%=16.65, 167@10%=16.70 → total 33.35
        var tiers = new List<RateTier>
        {
            new() { From = 0m,   To = 333m, Rate = 0.05m },
            new() { From = 333m, To = null,  Rate = 0.10m }
        };

        var result = CommissionCalculator.ComputeTieredCommission(
            Money.Of(500m, "EUR"), tiers);

        result.Amount.Should().Be(33.3500m);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ATTAINMENT-BASED rate table
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeAttainmentCommission_BelowLowestTier_ReturnsZero()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.50m, AttainmentTo = null, Rate = 0.07m }
        };

        var result = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, attainmentPct: 0.30m);

        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void ComputeAttainmentCommission_AttainmentInMiddleTier_UsesCorrectRate()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.00m, AttainmentTo = 0.49m, Rate = 0.00m },
            new() { AttainmentFrom = 0.50m, AttainmentTo = 0.99m, Rate = 0.07m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,   Rate = 0.12m }
        };

        var result = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, attainmentPct: 0.75m);

        result.Amount.Should().Be(70.0000m);
    }

    [Fact]
    public void ComputeAttainmentCommission_AttainmentAtExactLowerBound_Matches()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.50m, AttainmentTo = 0.99m, Rate = 0.07m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,   Rate = 0.12m }
        };

        var result = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, attainmentPct: 0.50m);

        result.Amount.Should().Be(70.0000m);
    }

    [Fact]
    public void ComputeAttainmentCommission_AttainmentAtExactUpperBound_StaysInTier()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.50m, AttainmentTo = 0.99m, Rate = 0.07m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,   Rate = 0.12m }
        };

        var result = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, attainmentPct: 0.99m);

        result.Amount.Should().Be(70.0000m);
    }

    [Fact]
    public void ComputeAttainmentCommission_AttainmentAt100Pct_PicksHigherTier()
    {
        // 1.00 matches both [0.50,1.00] (if upper-inclusive) AND [1.00,null].
        // LastOrDefault picks the LAST match — the higher tier wins.
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.50m, AttainmentTo = 1.00m, Rate = 0.07m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,   Rate = 0.12m }
        };

        var result = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, attainmentPct: 1.00m);

        result.Amount.Should().Be(120.0000m);
    }

    [Fact]
    public void ComputeAttainmentCommission_AboveAllTiers_OpenEndedLastTierMatches()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.50m, AttainmentTo = 0.99m, Rate = 0.07m },
            new() { AttainmentFrom = 1.00m, AttainmentTo = null,   Rate = 0.12m }
        };

        var result = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, attainmentPct: 1.50m);

        result.Amount.Should().Be(120.0000m);
    }

    [Fact]
    public void ComputeAttainmentCommission_ZeroAttainment_BelowAllTiers_ReturnsZero()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.50m, AttainmentTo = null, Rate = 0.07m }
        };

        var result = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, attainmentPct: 0.00m);

        result.Amount.Should().Be(0m);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MODIFIER
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyModifier_NullModifier_ReturnsCommissionUnchanged()
    {
        var commission = Money.Of(100m, "EUR");
        var result = CommissionCalculator.ApplyModifier(commission, Money.Of(1000m, "EUR"), null);

        result.Amount.Should().Be(100m);
    }

    [Fact]
    public void ApplyModifier_MultiplierFactor_MultipliesCommission()
    {
        var commission = Money.Of(100m, "EUR");
        var modifier = new Modifier { Type = ModifierType.Multiplier, Factor = 1.5m };

        var result = CommissionCalculator.ApplyModifier(commission, Money.Of(1000m, "EUR"), modifier);

        result.Amount.Should().Be(150.0000m);
    }

    [Fact]
    public void ApplyModifier_AcceleratorFactor_TreatedAsMultiplier()
    {
        var commission = Money.Of(100m, "EUR");
        var modifier = new Modifier { Type = ModifierType.Accelerator, Factor = 2.0m };

        var result = CommissionCalculator.ApplyModifier(commission, Money.Of(1000m, "EUR"), modifier);

        result.Amount.Should().Be(200.0000m);
    }

    [Fact]
    public void ApplyModifier_SpiffFactor_TreatedAsMultiplier()
    {
        var commission = Money.Of(100m, "EUR");
        var modifier = new Modifier { Type = ModifierType.Spiff, Factor = 0.5m };

        var result = CommissionCalculator.ApplyModifier(commission, Money.Of(1000m, "EUR"), modifier);

        result.Amount.Should().Be(50.0000m);
    }

    [Fact]
    public void ApplyModifier_ZeroFactor_ReturnsZeroCommission()
    {
        var commission = Money.Of(100m, "EUR");
        var modifier = new Modifier { Type = ModifierType.Multiplier, Factor = 0m };

        var result = CommissionCalculator.ApplyModifier(commission, Money.Of(1000m, "EUR"), modifier);

        result.Amount.Should().Be(0m);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CAP
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyCap_NullCap_ReturnsCommissionUnchanged()
    {
        var commission = Money.Of(100m, "EUR");
        var result = CommissionCalculator.ApplyCap(commission, null);

        result.Amount.Should().Be(100m);
    }

    [Fact]
    public void ApplyCap_PerTransaction_CommissionAboveCap_ReturnsCap()
    {
        var commission = Money.Of(100m, "EUR");
        var cap = new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(50m, "EUR") };

        var result = CommissionCalculator.ApplyCap(commission, cap);

        result.Amount.Should().Be(50.0000m);
    }

    [Fact]
    public void ApplyCap_PerTransaction_CommissionBelowCap_ReturnsCommission()
    {
        var commission = Money.Of(30m, "EUR");
        var cap = new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(50m, "EUR") };

        var result = CommissionCalculator.ApplyCap(commission, cap);

        result.Amount.Should().Be(30.0000m);
    }

    [Fact]
    public void ApplyCap_PerTransaction_CommissionEqualToCap_ReturnsCap()
    {
        var commission = Money.Of(50m, "EUR");
        var cap = new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(50m, "EUR") };

        var result = CommissionCalculator.ApplyCap(commission, cap);

        result.Amount.Should().Be(50.0000m);
    }

    [Fact]
    public void ApplyCap_PerPeriod_Deferred_PassesThroughCommission()
    {
        var commission = Money.Of(200m, "EUR");
        var cap = new Cap { Scope = CapScope.PerPeriod, Amount = Money.Of(100m, "EUR") };

        var result = CommissionCalculator.ApplyCap(commission, cap);

        result.Amount.Should().Be(200m);
    }

    [Fact]
    public void ApplyCap_Total_Deferred_PassesThroughCommission()
    {
        var commission = Money.Of(500m, "EUR");
        var cap = new Cap { Scope = CapScope.Total, Amount = Money.Of(300m, "EUR") };

        var result = CommissionCalculator.ApplyCap(commission, cap);

        result.Amount.Should().Be(500m);
    }

    [Fact]
    public void ApplyCap_PerTransaction_CurrencyMismatch_SkipsCap()
    {
        var commission = Money.Of(200m, "EUR");
        var cap = new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(50m, "USD") };

        var result = CommissionCalculator.ApplyCap(commission, cap);

        result.Amount.Should().Be(200m);
        result.Currency.Should().Be("EUR");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FLOOR
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFloor_NullFloor_ReturnsCommissionUnchanged()
    {
        var commission = Money.Of(5m, "EUR");
        var result = CommissionCalculator.ApplyFloor(commission, null);

        result.Amount.Should().Be(5m);
    }

    [Fact]
    public void ApplyFloor_CommissionBelowFloor_ReturnsFloor()
    {
        var commission = Money.Of(5m, "EUR");
        var floor = new Floor { Amount = Money.Of(20m, "EUR") };

        var result = CommissionCalculator.ApplyFloor(commission, floor);

        result.Amount.Should().Be(20.0000m);
    }

    [Fact]
    public void ApplyFloor_CommissionAboveFloor_ReturnsCommission()
    {
        var commission = Money.Of(100m, "EUR");
        var floor = new Floor { Amount = Money.Of(20m, "EUR") };

        var result = CommissionCalculator.ApplyFloor(commission, floor);

        result.Amount.Should().Be(100.0000m);
    }

    [Fact]
    public void ApplyFloor_CommissionEqualToFloor_ReturnsCommission()
    {
        var commission = Money.Of(20m, "EUR");
        var floor = new Floor { Amount = Money.Of(20m, "EUR") };

        var result = CommissionCalculator.ApplyFloor(commission, floor);

        result.Amount.Should().Be(20.0000m);
    }

    [Fact]
    public void ApplyFloor_CurrencyMismatch_SkipsFloor()
    {
        var commission = Money.Of(1m, "EUR");
        var floor = new Floor { Amount = Money.Of(100m, "USD") };

        var result = CommissionCalculator.ApplyFloor(commission, floor);

        result.Amount.Should().Be(1m);
        result.Currency.Should().Be("EUR");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TRIGGER EVALUATION
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EvaluateTrigger_NoConditions_AlwaysReturnsTrue()
    {
        var tx = MakeTx(1000m);
        var result = CommissionCalculator.EvaluateTrigger(Trigger.Always(), tx);

        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_AmountGreaterThan_Met()
    {
        var tx = MakeTx(1000m);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_AmountGreaterThan_NotMet()
    {
        var tx = MakeTx(200m);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeFalse();
    }

    [Fact]
    public void EvaluateTrigger_AmountEqual_Exact()
    {
        var tx = MakeTx(500m);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.Equal,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_AND_BothConditionsMet_ReturnsTrue()
    {
        var tx = MakeTx(1000m);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
            },
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.LessThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "5000" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_AND_OneConditionFails_ReturnsFalse()
    {
        var tx = MakeTx(200m);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
            },
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.LessThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "5000" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeFalse();
    }

    [Fact]
    public void EvaluateTrigger_OR_OneConditionMet_ReturnsTrue()
    {
        var tx = MakeTx(1000m);
        var trigger = Trigger.When(LogicalOperator.Or, [
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "5000" }   // false
            },
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }    // true
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_OR_NoConditionMet_ReturnsFalse()
    {
        var tx = MakeTx(100m);
        var trigger = Trigger.When(LogicalOperator.Or, [
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "5000" }
            },
            new Condition
            {
                Field = "TransactionAmount",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Number, Raw = "500" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeFalse();
    }

    [Fact]
    public void EvaluateTrigger_DateCondition_Equal_Met()
    {
        var tx = MakeTx(100m);  // TxDate = 2026-03-15
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "TransactionDate",
                Operator = ConditionOperator.Equal,
                Value = new ConditionValue { Type = ConditionValueType.Date, Raw = "2026-03-15" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_DateCondition_GreaterThan_NotMet()
    {
        var tx = MakeTx(100m);  // TxDate = 2026-03-15
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "TransactionDate",
                Operator = ConditionOperator.GreaterThan,
                Value = new ConditionValue { Type = ConditionValueType.Date, Raw = "2026-12-31" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeFalse();
    }

    [Fact]
    public void EvaluateTrigger_SourceStringEqual_Met()
    {
        var tx = MakeTx(100m, source: TransactionSource.Manual);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "Source",
                Operator = ConditionOperator.Equal,
                Value = new ConditionValue { Type = ConditionValueType.String, Raw = "Manual" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_SourceStringIn_Met()
    {
        var tx = MakeTx(100m, source: TransactionSource.EtlImport);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "Source",
                Operator = ConditionOperator.In,
                Value = new ConditionValue
                {
                    Type = ConditionValueType.String,
                    Raw = "EtlImport",
                    Set = ["Manual", "EtlImport", "ApiIngestion"]
                }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_SourceStringNotIn_Met()
    {
        var tx = MakeTx(100m, source: TransactionSource.Manual);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "Source",
                Operator = ConditionOperator.NotIn,
                Value = new ConditionValue
                {
                    Type = ConditionValueType.String,
                    Raw = "",
                    Set = ["EtlImport", "ApiIngestion"]
                }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeTrue();
    }

    [Fact]
    public void EvaluateTrigger_UnknownField_ReturnsFalse()
    {
        var tx = MakeTx(100m);
        var trigger = Trigger.When(LogicalOperator.And, [
            new Condition
            {
                Field = "NonExistentField",
                Operator = ConditionOperator.Equal,
                Value = new ConditionValue { Type = ConditionValueType.String, Raw = "anything" }
            }
        ]);

        CommissionCalculator.EvaluateTrigger(trigger, tx).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ROUNDING PRECISION — banker's rounding at 4 decimals
    // ═══════════════════════════════════════════════════════════════════════

    // InlineData uses strings because decimal literals are not valid attribute arguments.
    [Theory]
    [InlineData("1", "0.33335", "0.3334")]   // 5th decimal=5, 4th=3 (odd) → round up
    [InlineData("1", "0.33325", "0.3332")]   // 5th decimal=5, 4th=2 (even) → round down (banker's)
    [InlineData("1", "0.33333", "0.3333")]   // 5th decimal=3 → truncate
    [InlineData("1", "0.33336", "0.3334")]   // 5th decimal=6 → round up
    public void ComputeCommission_Flat_BankersRoundingAtFourthDecimal(
        string baseAmtS, string rateS, string expectedS)
    {
        var baseAmt  = decimal.Parse(baseAmtS,  System.Globalization.CultureInfo.InvariantCulture);
        var rate     = decimal.Parse(rateS,     System.Globalization.CultureInfo.InvariantCulture);
        var expected = decimal.Parse(expectedS, System.Globalization.CultureInfo.InvariantCulture);

        var result = CommissionCalculator.ComputeCommission(
            Money.Of(baseAmt, "EUR"),
            RateTable.Flat(rate),
            attainmentPct: 1.0m);

        result.Amount.Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MULTI-CURRENCY ISOLATION
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeCommission_Flat_PreservesCurrency()
    {
        var result = CommissionCalculator.ComputeCommission(
            Money.Of(1000m, "PLN"),
            RateTable.Flat(0.05m),
            attainmentPct: 1.0m);

        result.Currency.Should().Be("PLN");
        result.Amount.Should().Be(50m);
    }

    [Fact]
    public void ApplyCap_SameCurrency_AppliesCorrectly()
    {
        var commission = Money.Of(100m, "PLN");
        var cap = new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(50m, "PLN") };

        CommissionCalculator.ApplyCap(commission, cap).Amount.Should().Be(50m);
    }

    [Fact]
    public void ApplyCap_CrossCurrencyDoesNotMix()
    {
        // EUR commission vs USD cap — each currency is independent, cap must not apply
        var eurCommission = Money.Of(200m, "EUR");
        var usdCap = new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(50m, "USD") };

        var result = CommissionCalculator.ApplyCap(eurCommission, usdCap);

        result.Currency.Should().Be("EUR");
        result.Amount.Should().Be(200m);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DETERMINISM
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeCommission_SameInputTwice_IdenticalResult()
    {
        var tiers = new List<RateTier>
        {
            new() { From = 0m,   To = 500m, Rate = 0.05m },
            new() { From = 500m, To = null,  Rate = 0.10m }
        };

        var a = CommissionCalculator.ComputeCommission(
            Money.Of(750m, "EUR"), RateTable.Tiered(tiers), 1.0m);
        var b = CommissionCalculator.ComputeCommission(
            Money.Of(750m, "EUR"), RateTable.Tiered(tiers), 1.0m);

        a.Amount.Should().Be(b.Amount);
        a.Currency.Should().Be(b.Currency);
    }

    [Fact]
    public void ComputeCommission_Attainment_SameInputTwice_IdenticalResult()
    {
        var tiers = new List<AttainmentTier>
        {
            new() { AttainmentFrom = 0.50m, AttainmentTo = null, Rate = 0.10m }
        };

        var a = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, 0.80m);
        var b = CommissionCalculator.ComputeAttainmentCommission(
            Money.Of(1000m, "EUR"), tiers, 0.80m);

        a.Amount.Should().Be(b.Amount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PIPELINE: ComputeCommission → ApplyModifier → ApplyCap → ApplyFloor
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Pipeline_Flat_WithModifier_WithCap_ReturnsExpected()
    {
        // base=1000, rate=10% → commission=100
        // modifier x1.5 → 150
        // cap=120 → 120
        var commission = CommissionCalculator.ComputeCommission(
            Money.Of(1000m, "EUR"), RateTable.Flat(0.10m), 1.0m);

        commission = CommissionCalculator.ApplyModifier(
            commission,
            Money.Of(1000m, "EUR"),
            new Modifier { Type = ModifierType.Multiplier, Factor = 1.5m });

        commission = CommissionCalculator.ApplyCap(
            commission,
            new Cap { Scope = CapScope.PerTransaction, Amount = Money.Of(120m, "EUR") });

        commission.Amount.Should().Be(120.0000m);
    }

    [Fact]
    public void Pipeline_Flat_BelowFloor_FloorApplied()
    {
        // base=10, rate=1% → commission=0.10
        // floor=5 → 5
        var commission = CommissionCalculator.ComputeCommission(
            Money.Of(10m, "EUR"), RateTable.Flat(0.01m), 1.0m);

        commission = CommissionCalculator.ApplyFloor(
            commission,
            new Floor { Amount = Money.Of(5m, "EUR") });

        commission.Amount.Should().Be(5.0000m);
    }

    [Fact]
    public void Pipeline_AllStepsNoop_CommissionPassesThroughUnchanged()
    {
        // modifier=null, cap=null, floor=null → identity chain
        var commission = CommissionCalculator.ComputeCommission(
            Money.Of(1000m, "EUR"), RateTable.Flat(0.05m), 1.0m);

        commission = CommissionCalculator.ApplyModifier(commission, Money.Of(1000m, "EUR"), null);
        commission = CommissionCalculator.ApplyCap(commission, null);
        commission = CommissionCalculator.ApplyFloor(commission, null);

        commission.Amount.Should().Be(50.0000m);
    }
}
