using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Subscription;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable("UserSubscriptions");

        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.TenantId)
            .IsUnique(); // one active subscription per tenant

        builder.Property(s => s.TenantId)
            .IsRequired();

        builder.Property(s => s.Tier)
            .IsRequired();

        builder.Property(s => s.Status)
            .IsRequired();

        builder.Property(s => s.BillingEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(s => s.StripeSubscriptionId)
            .HasMaxLength(100);

        builder.Property(s => s.StripeCustomerId)
            .HasMaxLength(100);

        builder.Property(s => s.StripePriceId)
            .HasMaxLength(100);

        builder.Property(s => s.StripeProductId)
            .HasMaxLength(100);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasOne<Wasnie.Domain.Entities.Tenant>()
            .WithOne()
            .HasForeignKey<UserSubscription>(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
