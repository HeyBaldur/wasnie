using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Assistant;

namespace Wasnie.Infrastructure.Persistence.Configurations.Assistant;

public sealed class AssistantBoostDebitConfiguration : IEntityTypeConfiguration<AssistantBoostDebit>
{
    public void Configure(EntityTypeBuilder<AssistantBoostDebit> builder)
    {
        builder.ToTable("AssistantBoostDebits");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.PeriodStart).IsRequired();
        builder.Property(d => d.PeriodEnd).IsRequired();
        builder.Property(d => d.IncludedLimit).IsRequired();
        builder.Property(d => d.UsedInPeriod).IsRequired();
        builder.Property(d => d.Tokens).IsRequired();
        builder.Property(d => d.CreatedAt).IsRequired();

        // ★★ ONE CLOSE PER PERIOD. A redelivered renewal webhook, a reconciler run twice or two instances racing would
        // otherwise charge the tenant's boost a second time for the same month. The unique key makes the repeat fail.
        builder.HasIndex(d => new { d.TenantId, d.PeriodStart })
            .IsUnique()
            .HasDatabaseName("UX_AssistantBoostDebits_TenantId_PeriodStart");
    }
}
