using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PlanAssignmentActivatedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid AssignmentId,
    Guid PlanId,
    Guid PayeeId,
    Guid TenantId) : IDomainEvent;
