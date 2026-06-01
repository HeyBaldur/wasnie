using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record TransactionPayeeReassignedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid TransactionId,
    Guid TenantId,
    Guid OldPayeeId,
    Guid NewPayeeId,
    string Reason) : IDomainEvent;
