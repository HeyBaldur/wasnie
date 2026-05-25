using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PayoutCalculatedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid PayoutId,
    Guid PayeeId,
    Guid TenantId) : IDomainEvent;
