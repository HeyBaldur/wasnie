namespace Wasnie.Domain.Common;

public abstract class BaseAuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}
