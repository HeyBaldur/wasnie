using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Enrichment;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class CategoryMappingConfiguration : IEntityTypeConfiguration<CategoryMapping>
{
    public void Configure(EntityTypeBuilder<CategoryMapping> builder)
    {
        builder.ToTable("CategoryMappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.InputField).IsRequired().HasMaxLength(100);
        builder.Property(m => m.InputValue).IsRequired().HasMaxLength(CategoryMapping.MaxInputValueLength);
        builder.Property(m => m.Category).IsRequired().HasMaxLength(CategoryMapping.MaxCategoryLength);

        // HARD collision rule: two mappings claiming the same (InputField, InputValue) for one tenant
        // must never coexist — that is precedence-by-luck, the exact silence this layer exists to kill.
        // Enforced case-insensitively at the DB so "LAP-12" and "lap-12" collide (matching is CI too).
        builder.HasIndex(m => new { m.TenantId, m.InputField, m.InputValue })
            .IsUnique()
            .HasDatabaseName("IX_CategoryMappings_TenantId_InputField_InputValue");
    }
}
