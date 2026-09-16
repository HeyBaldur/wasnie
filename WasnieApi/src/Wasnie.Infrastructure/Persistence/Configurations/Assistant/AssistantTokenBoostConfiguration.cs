using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Assistant;

namespace Wasnie.Infrastructure.Persistence.Configurations.Assistant;

public sealed class AssistantTokenBoostConfiguration : IEntityTypeConfiguration<AssistantTokenBoost>
{
    public void Configure(EntityTypeBuilder<AssistantTokenBoost> builder)
    {
        builder.ToTable("AssistantTokenBoosts");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.StripeEventId).IsRequired().HasMaxLength(AssistantTokenBoost.MaxStripeIdLength);
        builder.Property(b => b.StripeProductId).IsRequired().HasMaxLength(AssistantTokenBoost.MaxStripeIdLength);
        builder.Property(b => b.StripeSessionId).HasMaxLength(AssistantTokenBoost.MaxStripeIdLength);
        builder.Property(b => b.Tokens).IsRequired();
        builder.Property(b => b.PurchasedAt).IsRequired();
        builder.Property(b => b.ExpiresAt).IsRequired();

        // ★★ THE DOUBLE-CREDIT GUARD. Stripe redelivers events, and the webhook is not the only thing that may credit a
        // purchase. This index is what makes the second attempt FAIL instead of doubling a customer's balance — the
        // check-then-insert in code cannot do it alone, because two deliveries can race past the check. Unfiltered on
        // purpose: an event id is unique across every tenant.
        builder.HasIndex(b => b.StripeEventId)
            .IsUnique()
            .HasDatabaseName("UX_AssistantTokenBoosts_StripeEventId");

        // The balance walk reads every lot a tenant ever bought, oldest first.
        builder.HasIndex(b => new { b.TenantId, b.PurchasedAt })
            .HasDatabaseName("IX_AssistantTokenBoosts_TenantId_PurchasedAt");
    }
}
