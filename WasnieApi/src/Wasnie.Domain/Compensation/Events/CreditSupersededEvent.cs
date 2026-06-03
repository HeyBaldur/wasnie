using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record CreditSupersededEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid CreditId,
    Guid TransactionId,
    Guid PayeeId,
    Guid TenantId,
    string Reason) : IDomainEvent;
