using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record TransactionIngestedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid TransactionId,
    Guid TenantId,
    string ReferenceNumber) : IDomainEvent;
