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
            correlationId: entry.CorrelationId,
            // ★★ THIS LINE WAS MISSING, AND THE COLUMN HAS BEEN EMPTY SINCE THE TABLE EXISTED.
            // AuditEntry has carried Metadata all along and AuditLog has had the column all along; the
            // one place that joins them did not pass it, so every caller that carefully built a
            // dictionary — CreateManualLedgerAdjustmentHandler among them, with the signed amount, the
            // currency and the resulting balance — was writing it into nothing. Discovered because the
            // account-closure test asked the row what it had closed and the row said null.
            //
            // Serialised as JSON because the column is nvarchar(max) and a dictionary has to become
            // text somehow; JSON is what BeforeJson/AfterJson already are, so a reader needs one habit.
            metadata: entry.Metadata is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(entry.Metadata));

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }
}
