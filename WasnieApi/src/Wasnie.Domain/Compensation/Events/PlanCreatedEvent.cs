using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PlanCreatedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid PlanId, Guid TenantId) : IDomainEvent;
