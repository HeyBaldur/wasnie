using Wasnie.Domain.Common;
using Wasnie.Domain.Enums;

namespace Wasnie.Domain.Entities;

public sealed class Plan : BaseAuditableEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveTo { get; private set; }
    public PlanStatus Status { get; private set; } = PlanStatus.Draft;

    private Plan() { }

    public static Plan Create(
        Guid tenantId,
        string name,
        string description,
        DateOnly effectiveFrom,
        DateOnly effectiveTo,
        string createdBy)
    {
        return new Plan
        {
            TenantId = tenantId,
            Name = name,
            Description = description,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = createdBy
        };
    }

    public void Activate()
    {
        if (Status != PlanStatus.Draft)
        {
            return;
        }

        Status = PlanStatus.Active;
    }

    public void Archive() => Status = PlanStatus.Archived;
}
