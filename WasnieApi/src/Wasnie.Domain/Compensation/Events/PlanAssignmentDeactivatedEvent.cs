using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PlanAssignmentDeactivatedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid AssignmentId,
    Guid TenantId) : IDomainEvent;
