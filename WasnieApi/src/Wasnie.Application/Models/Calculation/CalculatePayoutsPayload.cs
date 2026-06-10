namespace Wasnie.Application.Models.Calculation;

public sealed record CalculatePayoutsPayload(
    Guid TenantId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? PayeeIdFilter,
    string TriggeredBy,
    string TriggeredByEmail);
