using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Identity;

namespace Wasnie.Infrastructure.Persistence.Configurations.Identity;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Role)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(i => i.TokenHash)
            .IsRequired()
            .HasMaxLength(64); // SHA256 hex = 64 chars, same as the other token tables

        // No foreign key to Payees on purpose: the payee could be deleted between sending and
        // accepting, and a cascade would take the invitation with it. The accept path re-checks that
        // the payee still exists and is still unlinked, which is the check that actually matters.
        builder.Property(i => i.PayeeId);

        builder.Property(i => i.InvitedBy)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(i => i.AcceptedUserId).HasMaxLength(450);
        builder.Property(i => i.RevokedBy).HasMaxLength(450);

        builder.Property(i => i.ExpiresAt).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();

        // The accept endpoint is public and arrives with nothing but a token, so this is the one
        // lookup that cannot be narrowed by tenant first. It has to be indexed on its own.
        builder.HasIndex(i => i.TokenHash);

        // Listing a tenant's invitations, and checking whether this email already has a live one.
        builder.HasIndex(i => new { i.TenantId, i.Email });

        // ★ NO UNIQUE INDEX ON (TenantId, Email), DELIBERATELY. The same address may legitimately be
        // invited again after the first invitation was revoked or left to expire, and a person who
        // leaves and is invited back years later would otherwise collide with their own history.
        // "Only one LIVE invitation per address" is a rule about time, which an index cannot express;
        // it is enforced where the invitation is created, and §D1 keeps it off the read path.
    }
}
