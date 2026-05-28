using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record TransactionMarkedEligibleEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid TransactionId,
    Guid TenantId) : IDomainEvent;
