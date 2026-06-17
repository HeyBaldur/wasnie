using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record TransactionMarkedPaidEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid TransactionId,
    Guid TenantId,
    Guid PayeeId) : IDomainEvent;
