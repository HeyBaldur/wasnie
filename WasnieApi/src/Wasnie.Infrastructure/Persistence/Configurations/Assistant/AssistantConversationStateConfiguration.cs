using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Assistant;

namespace Wasnie.Infrastructure.Persistence.Configurations.Assistant;

public sealed class AssistantConversationStateConfiguration
    : IEntityTypeConfiguration<AssistantConversationState>
{
    public void Configure(EntityTypeBuilder<AssistantConversationState> builder)
    {
        builder.ToTable("AssistantConversationStates");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.UserId).IsRequired().HasMaxLength(450);
        builder.Property(s => s.ConversationId).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // ★ ONE ROW PER (USER, CONVERSATION), ENFORCED BY THE DATABASE. Two concurrent pins of the same
        // thread — a double click, or the drawer and the page both open — would otherwise each find no
        // row and each insert one, and from then on the pair has two standings that disagree. The
        // handler checks first; this is what makes the check true under a race rather than usually true.
        builder.HasIndex(s => new { s.UserId, s.ConversationId })
            .IsUnique()
            .HasDatabaseName("UX_AssistantConversationStates_UserId_ConversationId");

        // The one query this table serves: my pinned conversations, most recently pinned first.
        //
        // ★ FILTERED, so the index holds only the pinned rows. Unpinning keeps the row (it is this
        // user's standing, and it will hold more facts than the pin soon), so without the filter the
        // index would grow with every conversation anybody ever unpinned — rows that this query, the
        // only one it exists for, can never return.
        builder.HasIndex(s => new { s.TenantId, s.UserId, s.PinnedAt })
            .IsDescending(false, false, true)
            .HasFilter("[PinnedAt] IS NOT NULL")
            .HasDatabaseName("IX_AssistantConversationStates_TenantId_UserId_PinnedAt");

        // ★ CASCADE, AND THE HANDLER DELETES EXPLICITLY TOO — the same pairing the messages use, for the
        // same reason: the InMemory provider does not enforce cascades, so a test would pass against a
        // database that leaves orphans behind. See DeleteConversationHandler.
        builder.HasOne<AssistantConversation>()
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
