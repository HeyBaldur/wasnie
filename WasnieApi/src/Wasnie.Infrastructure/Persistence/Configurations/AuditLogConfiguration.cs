using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Audit;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .UseIdentityColumn();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TimestampUtc).IsRequired();
        builder.Property(x => x.ActorUserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ActorEmail).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ResourceType).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ResourceId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ResourceDisplayName).HasMaxLength(500);
        builder.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.AfterJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.UserAgent).HasMaxLength(500);
        builder.Property(x => x.Metadata).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => new { x.TenantId, x.TimestampUtc })
            .HasDatabaseName("IX_AuditLogs_TenantId_TimestampUtc");

        builder.HasIndex(x => new { x.TenantId, x.ResourceType, x.ResourceId, x.TimestampUtc })
            .HasDatabaseName("IX_AuditLogs_TenantId_ResourceType_ResourceId_TimestampUtc");

        builder.HasIndex(x => new { x.TenantId, x.ActorUserId, x.TimestampUtc })
            .HasDatabaseName("IX_AuditLogs_TenantId_ActorUserId_TimestampUtc");
    }
}
