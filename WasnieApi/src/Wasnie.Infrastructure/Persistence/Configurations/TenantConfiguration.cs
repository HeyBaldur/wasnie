using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Entities;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(t => t.Slug)
            .IsUnique();

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.Tier)
            .IsRequired();

        builder.Property(t => t.HasSelectedPlan)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.IsQualified)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.QualifiedAt);
        builder.Property(t => t.Country).HasMaxLength(100);
        builder.Property(t => t.PhoneNumber).HasMaxLength(50);
        builder.Property(t => t.HowHeardAboutUs).HasMaxLength(100);
        builder.Property(t => t.SalesVolumeRange).HasMaxLength(50);
        builder.Property(t => t.CurrentSystem).HasMaxLength(100);
        builder.Property(t => t.LegalAcceptedAt);
        builder.Property(t => t.LegalAcceptedVersion).HasMaxLength(20);
    }
}
