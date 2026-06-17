using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record CreditConsumedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid CreditId,
    Guid TransactionId,
    Guid PayeeId,
    Guid TenantId,
    Guid ConsumedByPayoutId) : IDomainEvent;
