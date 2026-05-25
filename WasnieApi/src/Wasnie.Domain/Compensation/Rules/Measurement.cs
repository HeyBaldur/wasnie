using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Domain.Compensation.Rules;

public sealed class Measurement
{
    public int _schema { get; init; } = 1;
    public MeasurementType Type { get; init; }
    public string SourceField { get; init; } = string.Empty;
    public MeasurementAggregation Aggregation { get; init; }
}
