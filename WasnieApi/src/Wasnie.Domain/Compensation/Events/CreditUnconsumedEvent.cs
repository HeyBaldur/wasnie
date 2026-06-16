using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record CreditUnconsumedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid CreditId,
    Guid TransactionId,
    Guid PayeeId,
    Guid TenantId,
    Guid FormerPayoutId) : IDomainEvent;
