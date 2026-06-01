using Wasnie.Domain.Common;

namespace Wasnie.Domain.Settings;

public sealed class FieldRequirementSetting : Entity
{
    public Guid TenantId { get; private set; }
    public string EntityName { get; private set; } = string.Empty;
    public string FieldName { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; }

    private FieldRequirementSetting() { }

    public static FieldRequirementSetting Create(
        Guid id,
        Guid tenantId,
        string entityName,
        string fieldName,
        bool isRequired) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            EntityName = entityName,
            FieldName = fieldName,
            IsRequired = isRequired,
        };

    public void SetRequired(bool isRequired)
    {
        IsRequired = isRequired;
    }
}
