using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Entities;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class ImportAuditConfiguration : IEntityTypeConfiguration<ImportAudit>
{
    public void Configure(EntityTypeBuilder<ImportAudit> builder)
    {
        builder.ToTable("ImportAudits");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ImportedBy).IsRequired().HasMaxLength(256);
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.CompletedAt).IsRequired();
        builder.Property(x => x.ResourceType).IsRequired().HasMaxLength(50);
        builder.Property(x => x.TotalRows).IsRequired();
        builder.Property(x => x.CreatedCount).IsRequired();
        builder.Property(x => x.SkippedCount).IsRequired();
        builder.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(30);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.StartedAt);
    }
}
