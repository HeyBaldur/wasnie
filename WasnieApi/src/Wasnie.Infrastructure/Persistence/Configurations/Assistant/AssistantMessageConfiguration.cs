using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Assistant;

namespace Wasnie.Infrastructure.Persistence.Configurations.Assistant;

public sealed class AssistantMessageConfiguration : IEntityTypeConfiguration<AssistantMessage>
{
    public void Configure(EntityTypeBuilder<AssistantMessage> builder)
    {
        builder.ToTable("AssistantMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConversationId).IsRequired();
        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Content).IsRequired().HasMaxLength(AssistantMessage.MaxContentLength);

        // Stored as text, like Role and for the same reason: a row read straight from the database says
        // "Cancelled" rather than "1", and adding a third outcome later cannot silently renumber the
        // two that already exist. Existing rows take the migration's default — every turn written
        // before this column existed did finish, so `Complete` is the truth for all of them and not a
        // convenient guess.
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Untyped JSON, nullable, unused in this piece. nvarchar(max) because what later pieces attach
        // (retrieved document references, screen context, a pre-fill payload) has no bound worth
        // guessing today — and guessing low is a migration over live chat history.
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)");

        builder.Property(m => m.Sequence).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        // Deleting a conversation takes its messages with it. Chat turns have no meaning without the
        // thread they belong to, so an orphan row is never the desired outcome.
        builder.HasOne<AssistantConversation>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Reading one conversation: its turns in the order they were written.
        builder.HasIndex(m => new { m.ConversationId, m.Sequence })
            .IsUnique()
            .HasDatabaseName("IX_AssistantMessages_ConversationId_Sequence");
    }
}
