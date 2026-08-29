using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Domain.Compensation.Events;

/// <summary>
/// A credit reached a terminal state without ever being paid through a payout: the account of a
/// departed payee was closed, and this credit was part of what got closed.
///
/// ★ ITS OWN EVENT, NOT CreditSupersededEvent. Superseded means "a reallocation replaced this one", and
/// the attainment queries read it that way. Raising it here would tell every one of those readers that
/// a replacement exists somewhere. There is none.
/// </summary>
public sealed record CreditClosedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid CreditId,
    Guid TransactionId,
    Guid PayeeId,
    Guid TenantId,
    CreditClosureReason Reason,
    decimal Amount,
    string Currency,
    string Note) : IDomainEvent;
