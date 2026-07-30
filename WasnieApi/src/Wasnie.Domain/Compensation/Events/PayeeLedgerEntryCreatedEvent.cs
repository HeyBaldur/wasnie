using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PayeeLedgerEntryCreatedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid EntryId,
    Guid TenantId,
    Guid PayeeId,
    decimal SignedAmount,
    string Currency,
    string TransactionType,
    string Origin) : IDomainEvent;
