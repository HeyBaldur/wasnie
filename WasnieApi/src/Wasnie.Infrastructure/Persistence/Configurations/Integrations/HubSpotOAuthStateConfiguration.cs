using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Integrations.HubSpot;

namespace Wasnie.Infrastructure.Persistence.Configurations.Integrations;

public sealed class HubSpotOAuthStateConfiguration : IEntityTypeConfiguration<HubSpotOAuthState>
{
    public void Configure(EntityTypeBuilder<HubSpotOAuthState> builder)
    {
        builder.ToTable("HubSpotOAuthStates");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.InitiatedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.ExpiresAt).IsRequired();
        builder.Property(s => s.UsedAt);

        // NOTE: no tenant query filter — the anonymous OAuth callback resolves the tenant FROM this row.
    }
}
