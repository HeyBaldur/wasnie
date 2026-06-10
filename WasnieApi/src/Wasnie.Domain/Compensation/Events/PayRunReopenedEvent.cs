using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PayRunReopenedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid PayRunId,
    Guid TenantId) : IDomainEvent;
