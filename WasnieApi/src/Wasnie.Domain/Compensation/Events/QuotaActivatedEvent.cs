using Wasnie.Domain.Common;

namespace Wasnie.Domain.Compensation.Events;

public sealed record QuotaActivatedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid QuotaId, Guid TenantId) : IDomainEvent;
