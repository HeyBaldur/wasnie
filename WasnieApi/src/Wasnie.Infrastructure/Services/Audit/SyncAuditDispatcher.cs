using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;

namespace Wasnie.Infrastructure.Services.Audit;

public sealed class SyncAuditDispatcher(IApplicationDbContext db, IClock clock) : IAuditDispatcher
{
    public async Task DispatchAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var log = AuditLog.Create(
            tenantId: entry.TenantId,
            timestampUtc: clock.UtcNow,
            actorUserId: entry.ActorUserId,
            actorEmail: entry.ActorEmail,
            action: entry.Action,
            resourceType: entry.ResourceType,
            resourceId: entry.ResourceId,
            resourceDisplayName: entry.DisplayName,
            beforeJson: entry.Before as string,
            afterJson: entry.After as string,
            correlationId: entry.CorrelationId);

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }
}
