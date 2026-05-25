using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PayoutApprovedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid PayoutId,
    Guid TenantId) : IDomainEvent;
