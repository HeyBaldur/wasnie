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

        // ★★ THE COLLATION IS THE SEARCH FEATURE. The list's search box matches on this column, and the
        // server's usual default (SQL_Latin1_General_CP1_CI_AS) is case-insensitive but accent-
        // SENSITIVE — so "asignacion" would not find "Asignación". These titles are written in Spanish,
        // English and Polish by people who do not reach for the accent key while searching, so an
        // accent-sensitive match is a search box that fails on the words it was built for.
        //
        // ★ ON THE COLUMN, NOT ON THE QUERY. Stated here it is a property of the data: every comparison
        // against a title behaves the same way, and the Application layer never has to know what SQL
        // Server calls its collations. Written as `EF.Functions.Collate` in one handler, the NEXT
        // handler somebody writes would silently be sensitive again.
        //
        // ★ KNOWN LIMIT, AND IT IS NOT A BUG TO FIX HERE: this collation does not fold the Polish ł to
        // l — it is a distinct letter, not an accented one. The front end's own folding does map it
        // (conversation-groups.ts), so a Polish title containing ł is found by typing ł and not by
        // typing l. Changing that means a Polish-specific collation on a column shared by three
        // languages, which is a trade with its own decision to make.
        builder.Property(c => c.Title)
            .IsRequired()
            .HasMaxLength(AssistantConversation.MaxTitleLength)
            .UseCollation(Application.Assistant.Common.AssistantPaging.SearchCollation);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // The ONLY list query this feature has: my conversations, most recently active first.
        // (TenantId, UserId) leads because both are always supplied — a conversation is never read
        // by tenant alone.
        //
        // ★ THE SORT DIRECTIONS ARE DECLARED, AND THAT IS THE POINT OF REPLACING THE OLD INDEX. The
        // previous one keyed (TenantId, UserId, UpdatedAt) ascending, which serves an ORDER BY DESC
        // perfectly well — a b-tree reads backwards. What it does NOT serve is the keyset seek this
        // list now makes: `WHERE UpdatedAt < @u OR (UpdatedAt = @u AND Id < @id)`. That predicate is a
        // seek on the composite key only when both columns are IN the key, in the order and direction
        // the query asks for. With Id absent, the tie half degrades to a scan of every row sharing a
        // timestamp.
        //
        // ★ AND Id IS NAMED EXPLICITLY EVEN THOUGH IT IS THE CLUSTERING KEY. SQL Server carries the
        // clustering key in every nonclustered leaf, so the column is physically there either way —
        // but it is not a KEY column, so it cannot be part of the seek predicate or the ordering. The
        // difference between "present" and "usable for a seek" is the whole reason this index changed.
        builder.HasIndex(c => new { c.TenantId, c.UserId, c.UpdatedAt, c.Id })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("IX_AssistantConversations_TenantId_UserId_UpdatedAt_Id");
    }
}
