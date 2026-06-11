using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record TransactionCancelledEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid TransactionId,
    Guid TenantId,
    string Reason) : IDomainEvent;
