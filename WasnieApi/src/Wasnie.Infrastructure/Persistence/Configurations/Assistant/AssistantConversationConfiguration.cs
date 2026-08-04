using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Assistant;

namespace Wasnie.Infrastructure.Persistence.Configurations.Assistant;

public sealed class AssistantConversationConfiguration : IEntityTypeConfiguration<AssistantConversation>
{
    public void Configure(EntityTypeBuilder<AssistantConversation> builder)
    {
        builder.ToTable("AssistantConversations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();

        // 450 = the ASP.NET Identity key width used by RefreshToken and the audit actor columns.
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(450);

        builder.Property(c => c.Title).IsRequired().HasMaxLength(AssistantConversation.MaxTitleLength);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // The ONLY list query this feature has: my conversations, most recently active first.
        // (TenantId, UserId) leads because both are always supplied — a conversation is never read
        // by tenant alone.
        builder.HasIndex(c => new { c.TenantId, c.UserId, c.UpdatedAt })
            .HasDatabaseName("IX_AssistantConversations_TenantId_UserId_UpdatedAt");
    }
}
