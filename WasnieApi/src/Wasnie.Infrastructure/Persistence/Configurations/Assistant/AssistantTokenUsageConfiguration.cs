using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Assistant;

namespace Wasnie.Infrastructure.Persistence.Configurations.Assistant;

public sealed class AssistantTokenUsageConfiguration : IEntityTypeConfiguration<AssistantTokenUsage>
{
    public void Configure(EntityTypeBuilder<AssistantTokenUsage> builder)
    {
        builder.ToTable("AssistantTokenUsages");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.TenantId).IsRequired();
        builder.Property(u => u.UserId).IsRequired().HasMaxLength(450);
        builder.Property(u => u.Call).IsRequired().HasMaxLength(32);
        builder.Property(u => u.Model).IsRequired().HasMaxLength(AssistantTokenUsage.MaxModelLength);
        builder.Property(u => u.Upstream).HasMaxLength(AssistantTokenUsage.MaxUpstreamLength);
        // Credits can be fractions of a millionth; the precision keeps a per-call figure summable without rounding away.
        builder.Property(u => u.Cost).HasPrecision(18, 10);
        builder.Property(u => u.CreatedAt).IsRequired();

        // ★ NO FOREIGN KEY TO THE CONVERSATION. A conversation can be deleted; the tokens it spent were still spent and
        // still billed. A cascade would erase consumption; a restrict would block deleting a thread.

        // The only reads: the tenant's total (trial allowance) and the tenant's total since a date (a paying account's
        // current billing period). Both are (TenantId, CreatedAt) range sums.
        builder.HasIndex(u => new { u.TenantId, u.CreatedAt })
            .HasDatabaseName("IX_AssistantTokenUsages_TenantId_CreatedAt");
    }
}
