using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Integrations.HubSpot;

namespace Wasnie.Infrastructure.Persistence.Configurations.Integrations;

public sealed class HubSpotConnectionConfiguration : IEntityTypeConfiguration<HubSpotConnection>
{
    public void Configure(EntityTypeBuilder<HubSpotConnection> builder)
    {
        builder.ToTable("HubSpotConnections");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.PortalId).IsRequired();

        // Encrypted token blobs (base64 of nonce+tag+ciphertext). Generously sized; never plaintext.
        builder.Property(c => c.AccessTokenEncrypted).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.RefreshTokenEncrypted).IsRequired().HasMaxLength(4000);

        builder.Property(c => c.TokenExpiresAt).IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();
        builder.Property(c => c.StatusReason).HasMaxLength(500);

        builder.Property(c => c.ConnectedAt).IsRequired();
        builder.Property(c => c.ConnectedBy).IsRequired().HasMaxLength(450);
        builder.Property(c => c.DisconnectedAt);
        builder.Property(c => c.DisconnectedBy).HasMaxLength(450);
        builder.Property(c => c.LastSyncedAt);
        builder.Property(c => c.UpdatedAt).IsRequired();

        builder.Property(c => c.RowVersion).IsRowVersion();

        // One HubSpot connection per tenant.
        builder.HasIndex(c => c.TenantId).IsUnique();
    }
}
