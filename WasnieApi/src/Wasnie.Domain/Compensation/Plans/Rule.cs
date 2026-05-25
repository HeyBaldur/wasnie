using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Rules;

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

    private Rule() { }

    internal static Rule Create(
        Guid planId,
        string name,
        int sortOrder,
        Trigger trigger,
        Measurement measurement,
        RateTable rateTable,
        Modifier? modifier,
        Cap? cap,
        Floor? floor) =>
        new()
        {
            Id = Guid.NewGuid(),
            PlanId = planId,
            Name = name,
            SortOrder = sortOrder,
            Trigger = trigger,
            Measurement = measurement,
            RateTable = rateTable,
            Modifier = modifier,
            Cap = cap,
            Floor = floor
        };

    internal void Update(
        string name,
        int sortOrder,
        Trigger trigger,
        Measurement measurement,
        RateTable rateTable,
        Modifier? modifier,
        Cap? cap,
        Floor? floor)
    {
        Name = name;
        SortOrder = sortOrder;
        Trigger = trigger;
        Measurement = measurement;
        RateTable = rateTable;
        Modifier = modifier;
        Cap = cap;
        Floor = floor;
    }

    internal void Deactivate() => IsActive = false;
}
