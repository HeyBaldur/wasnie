using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Settings;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("Favorites");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.TenantId).IsRequired();
        builder.Property(f => f.UserId).IsRequired().HasMaxLength(450);
        // By NAME: a reordered enum must not re-point stored favorites.
        builder.Property(f => f.EntityType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(f => f.EntityId).IsRequired();
        builder.Property(f => f.CreatedAt).IsRequired();

        // ★ ONE ROW PER (user, type, entity). A double-click that races two first writes fails loudly here instead of
        // leaving a duplicate that would count twice toward the limit. It is also the index of the only read: one
        // user's favorites of one type.
        builder.HasIndex(f => new { f.TenantId, f.UserId, f.EntityType, f.EntityId })
            .IsUnique()
            .HasDatabaseName("UX_Favorites_TenantId_UserId_EntityType_EntityId");
    }
}
