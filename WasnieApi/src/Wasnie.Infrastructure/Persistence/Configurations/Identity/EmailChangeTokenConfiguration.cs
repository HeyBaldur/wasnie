using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Identity;

namespace Wasnie.Infrastructure.Persistence.Configurations.Identity;

public sealed class EmailChangeTokenConfiguration : IEntityTypeConfiguration<EmailChangeToken>
{
    public void Configure(EntityTypeBuilder<EmailChangeToken> builder)
    {
        builder.ToTable("EmailChangeTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(t => t.NewEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64); // SHA256 hex = 64 chars

        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UsedAt);

        builder.HasIndex(t => new { t.UserId, t.TokenHash });
        builder.HasIndex(t => t.TokenHash);
    }
}
