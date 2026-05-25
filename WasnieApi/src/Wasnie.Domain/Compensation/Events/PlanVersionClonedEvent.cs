using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PlanVersionClonedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid SourcePlanId,
    Guid NewPlanId,
    int NewVersion,
    Guid TenantId) : IDomainEvent;
