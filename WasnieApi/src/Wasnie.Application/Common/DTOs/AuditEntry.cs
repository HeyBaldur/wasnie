namespace Wasnie.Application.Common.DTOs;

public sealed record AuditEntry(
    Guid TenantId,
    string Action,
    string ResourceType,
    string ResourceId,
    string ActorUserId,
    string ActorEmail,
    string? DisplayName = null,
    object? Before = null,
    object? After = null,
    string? CorrelationId = null,
    Dictionary<string, string>? Metadata = null);
