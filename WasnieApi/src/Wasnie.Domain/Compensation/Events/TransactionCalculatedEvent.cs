using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record TransactionCalculatedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid TransactionId,
    Guid PayeeId,
    Guid TenantId,
    int CreditCount,
    decimal TotalCommissionAmount,
    string TotalCommissionCurrency) : IDomainEvent;
