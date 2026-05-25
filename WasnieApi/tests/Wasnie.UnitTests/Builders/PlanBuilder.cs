using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.UnitTests.Builders;

internal sealed class PlanBuilder
{
    private Guid _tenantId = Guid.NewGuid();
    private string _name = "Test Plan";
    private string _description = "Test description";
    private DateOnly _start = new(2025, 1, 1);
    private DateOnly _end = new(2025, 12, 31);
    private string _currency = "EUR";
    private string _createdBy = "test-user";

    internal PlanBuilder WithTenantId(Guid id) { _tenantId = id; return this; }
    internal PlanBuilder WithName(string name) { _name = name; return this; }
    internal PlanBuilder WithCurrency(string currency) { _currency = currency; return this; }
    internal PlanBuilder WithPeriod(DateOnly start, DateOnly end) { _start = start; _end = end; return this; }

    internal Plan Build() => Plan.Create(_tenantId, _name, _description, DateRange.Of(_start, _end), _currency, _createdBy);

    internal Plan BuildWithOneRule()
    {
        var plan = Build();
        plan.AddRule(
            "Base Commission",
            sortOrder: 1,
            measurement: new Measurement { Type = MeasurementType.Revenue, SourceField = "amount", Aggregation = MeasurementAggregation.Sum },
            rateTable: RateTable.Flat(0.05m));
        return plan;
    }
}
