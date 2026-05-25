using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record QuotaClosedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid QuotaId, Guid TenantId) : IDomainEvent;
