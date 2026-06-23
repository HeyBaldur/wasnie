using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Infrastructure.Persistence.Configurations.Integrations;

public sealed class CrmOwnerMappingConfiguration : IEntityTypeConfiguration<CrmOwnerMapping>
{
    public void Configure(EntityTypeBuilder<CrmOwnerMapping> builder)
    {
        builder.ToTable("CrmOwnerMappings");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.Source).IsRequired().HasMaxLength(50);
        builder.Property(m => m.CrmOwnerId).IsRequired().HasMaxLength(100);
        builder.Property(m => m.PayeeId).IsRequired();
        builder.Property(m => m.MatchMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.CreatedBy).IsRequired().HasMaxLength(450);

        // One mapping per (tenant, source, owner) — an owner resolves to at most one payee per CRM.
        builder.HasIndex(m => new { m.TenantId, m.Source, m.CrmOwnerId })
            .IsUnique()
            .HasDatabaseName("IX_CrmOwnerMappings_TenantId_Source_CrmOwnerId");

        // Reverse lookup (which owners map to a payee).
        builder.HasIndex(m => new { m.TenantId, m.PayeeId })
            .HasDatabaseName("IX_CrmOwnerMappings_TenantId_PayeeId");
    }
}
