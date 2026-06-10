using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record PayRunPaidEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid PayRunId,
    Guid TenantId) : IDomainEvent;
