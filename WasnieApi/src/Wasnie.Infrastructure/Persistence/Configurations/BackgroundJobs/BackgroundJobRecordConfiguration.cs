using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.BackgroundJobs;

namespace Wasnie.Infrastructure.Persistence.Configurations.BackgroundJobs;

public sealed class BackgroundJobRecordConfiguration : IEntityTypeConfiguration<BackgroundJobRecord>
{
    public void Configure(EntityTypeBuilder<BackgroundJobRecord> builder)
    {
        builder.ToTable("BackgroundJobRecords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.HangfireJobId).HasMaxLength(200);
        builder.Property(x => x.HandlerType).IsRequired().HasMaxLength(500);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProgressCurrent).IsRequired();
        builder.Property(x => x.ProgressTotal).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.EnqueuedAtUtc).IsRequired();
        builder.Property(x => x.StartedAtUtc);
        builder.Property(x => x.CompletedAtUtc);

        builder.HasIndex(x => new { x.TenantId, x.State })
            .HasDatabaseName("IX_BackgroundJobRecords_TenantId_State");

        builder.HasIndex(x => new { x.TenantId, x.EnqueuedAtUtc })
            .HasDatabaseName("IX_BackgroundJobRecords_TenantId_EnqueuedAtUtc");
    }
}
