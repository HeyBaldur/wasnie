namespace Wasnie.Domain.Entities;

public sealed class ImportAudit
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ImportedBy { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public int TotalRows { get; private set; }
    public int CreatedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;

    private ImportAudit() { }

    public static ImportAudit Create(
        Guid id,
        Guid tenantId,
        string importedBy,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string resourceType,
        int totalRows,
        int createdCount,
        int skippedCount,
        string originalFileName,
        string status) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ImportedBy = importedBy,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ResourceType = resourceType,
            TotalRows = totalRows,
            CreatedCount = createdCount,
            SkippedCount = skippedCount,
            OriginalFileName = originalFileName,
            Status = status,
        };
}
