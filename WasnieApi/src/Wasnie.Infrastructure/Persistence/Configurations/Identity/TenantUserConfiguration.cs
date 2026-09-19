using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Identity;

namespace Wasnie.Infrastructure.Persistence.Configurations.Identity;

public sealed class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> builder)
    {
        builder.ToTable("TenantUsers");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.TenantId).IsRequired();

        builder.Property(u => u.UserId)
            .IsRequired()
            .HasMaxLength(450); // ASP.NET Identity's key width, as the token tables use

        builder.Property(u => u.Role)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(u => u.InvitedBy).HasMaxLength(450);
        builder.Property(u => u.DeactivatedBy).HasMaxLength(450);
        builder.Property(u => u.ReactivatedBy).HasMaxLength(450);

        builder.Property(u => u.CreatedAt).IsRequired();

        // One access row per person per tenant. Unique here and not merely checked in a handler,
        // because a duplicate would double-count a seat and show the same person twice on the screen.
        builder.HasIndex(u => new { u.TenantId, u.UserId }).IsUnique();

        // Sign-in asks "is this user's access open" with no tenant in hand yet.
        builder.HasIndex(u => u.UserId);

        // IsActive is a computed property with no setter and must not become a column: the timestamps
        // are the truth and TenantUser.ActiveSpec is the single rule for querying them.
        builder.Ignore(u => u.IsActive);
    }
}
