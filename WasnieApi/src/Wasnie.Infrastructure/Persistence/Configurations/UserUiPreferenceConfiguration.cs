using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Settings;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class UserUiPreferenceConfiguration : IEntityTypeConfiguration<UserUiPreference>
{
    public void Configure(EntityTypeBuilder<UserUiPreference> builder)
    {
        builder.ToTable("UserUiPreferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.UserId).IsRequired().HasMaxLength(450);
        builder.Property(p => p.Key).IsRequired().HasMaxLength(UserUiPreference.MaxKeyLength);
        builder.Property(p => p.Value).IsRequired().HasMaxLength(UserUiPreference.MaxValueLength);
        builder.Property(p => p.UpdatedAt).IsRequired();

        // ★ ONE ROW PER (user, key): the write is an upsert, and the index is what makes a double-click (two concurrent
        // first writes) fail loudly instead of leaving two rows that disagree. It also serves the only read: all of
        // one user's preferences.
        builder.HasIndex(p => new { p.TenantId, p.UserId, p.Key })
            .IsUnique()
            .HasDatabaseName("UX_UserUiPreferences_TenantId_UserId_Key");
    }
}
