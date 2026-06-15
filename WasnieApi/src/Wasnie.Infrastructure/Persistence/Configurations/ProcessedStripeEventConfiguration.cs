using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Subscription;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class ProcessedStripeEventConfiguration : IEntityTypeConfiguration<ProcessedStripeEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedStripeEvent> builder)
    {
        builder.ToTable("ProcessedStripeEvents");

        // EventId is the natural PK (Stripe event IDs are globally unique)
        builder.HasKey(e => e.EventId);

        builder.Property(e => e.EventId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.ProcessedAt)
            .IsRequired();

        // No tenant query filter — this is a global deduplication table
    }
}
