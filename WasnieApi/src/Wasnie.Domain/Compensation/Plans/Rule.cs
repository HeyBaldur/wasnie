using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Plans;

public sealed class Rule : Entity
{
    public Guid PlanId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public Trigger Trigger { get; private set; } = Trigger.Always();
    public Measurement Measurement { get; private set; } = new();
    public RateTable RateTable { get; private set; } = RateTable.Flat(0m);
    public Modifier? Modifier { get; private set; }
    public Cap? Cap { get; private set; }
    public Floor? Floor { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateRange? EffectivePeriod { get; private set; }
    public string? Tag { get; private set; }

    private Rule() { }

    private static void ValidateTag(string? tag)
    {
        if (tag != null && tag.Trim().Length > 50)
            throw new DomainException("Rule tag must not exceed 50 characters.");
    }

    // Units measurement calculates commission as ratePerUnit × quantity.
    // Tiered/Attainment require an amount base — incompatible with per-unit math in this version.
    private static void ValidateMeasurementRateTableCompatibility(Measurement measurement, RateTable rateTable)
    {
        if (measurement.Type == MeasurementType.Units && rateTable.Type != RateTableType.Flat)
            throw new DomainException(
                "Units measurement only supports a Flat rate table. " +
                "Tiered and Attainment rate tables are not supported for unit-based commission.");
    }

    internal static Rule Create(
        Guid planId,
        string name,
        int sortOrder,
        Trigger trigger,
        Measurement measurement,
        RateTable rateTable,
        Modifier? modifier,
        Cap? cap,
        Floor? floor,
        Guid id = default,
        DateRange? effectivePeriod = null,
        string? tag = null)
    {
        ValidateTag(tag);
        ValidateMeasurementRateTableCompatibility(measurement, rateTable);

        return new()
        {
            Id = id == default ? Guid.NewGuid() : id,
            PlanId = planId,
            Name = name,
            SortOrder = sortOrder,
            Trigger = trigger,
            Measurement = measurement,
            RateTable = rateTable,
            Modifier = modifier,
            Cap = cap,
            Floor = floor,
            EffectivePeriod = effectivePeriod,
            Tag = tag
        };
    }

    internal void Update(
        string name,
        int sortOrder,
        Trigger trigger,
        Measurement measurement,
        RateTable rateTable,
        Modifier? modifier,
        Cap? cap,
        Floor? floor,
        DateRange? effectivePeriod = null,
        string? tag = null)
    {
        ValidateTag(tag);
        ValidateMeasurementRateTableCompatibility(measurement, rateTable);

        Name = name;
        SortOrder = sortOrder;
        Trigger = trigger;
        Measurement = measurement;
        RateTable = rateTable;
        Modifier = modifier;
        Cap = cap;
        Floor = floor;
        EffectivePeriod = effectivePeriod;
        Tag = tag;
    }

    internal void Deactivate() => IsActive = false;
}
