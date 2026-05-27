namespace Wasnie.Domain.Audit;

public sealed class AuditLog
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public string ActorEmail { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string? ResourceDisplayName { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? Metadata { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid tenantId,
        DateTime timestampUtc,
        string actorUserId,
        string actorEmail,
        string action,
        string resourceType,
        string resourceId,
        string? resourceDisplayName = null,
        string? beforeJson = null,
        string? afterJson = null,
        string? correlationId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? metadata = null)
    {
        return new AuditLog
        {
            TenantId = tenantId,
            TimestampUtc = timestampUtc,
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ResourceDisplayName = resourceDisplayName,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            CorrelationId = correlationId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Metadata = metadata,
        };
    }
}
