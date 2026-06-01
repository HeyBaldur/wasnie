using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Settings;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class FieldRequirementSettingConfiguration : IEntityTypeConfiguration<FieldRequirementSetting>
{
    public void Configure(EntityTypeBuilder<FieldRequirementSetting> builder)
    {
        builder.ToTable("FieldRequirementSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(s => s.FieldName).IsRequired().HasMaxLength(100);
        builder.Property(s => s.IsRequired).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.EntityName, s.FieldName }).IsUnique();
    }
}
