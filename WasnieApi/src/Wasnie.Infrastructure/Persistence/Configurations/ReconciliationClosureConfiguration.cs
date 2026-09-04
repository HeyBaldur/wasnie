using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Reconciliation;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class ReconciliationClosureConfiguration : IEntityTypeConfiguration<ReconciliationClosure>
{
    public void Configure(EntityTypeBuilder<ReconciliationClosure> builder)
    {
        builder.ToTable("ReconciliationClosures");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.EntryKind).IsRequired();
        builder.Property(c => c.EntityId).IsRequired();
        builder.Property(c => c.Reason).IsRequired().HasMaxLength(64);
        builder.Property(c => c.FactOccurredAt).IsRequired();
        builder.Property(c => c.FactKey);

        // Long enough for a real explanation. An auditor's question is answered in a paragraph, not
        // in a tweet, and truncating the one field that carries the human reasoning would be the
        // worst place in this table to save bytes.
        builder.Property(c => c.Note).IsRequired().HasMaxLength(2000);

        builder.Property(c => c.PayeeId);
        builder.Property(c => c.ClosedAt).IsRequired();
        builder.Property(c => c.ClosedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(c => c.ClosedByEmail).HasMaxLength(256);

        // The shape the exclusion anti-join reads: every closure for a (kind, entity, reason),
        // newest fact first. Deliberately NOT unique — closing the same anomaly again after it
        // recurred is a second, legitimate fact, and a unique index would refuse to record it.
        builder.HasIndex(c => new { c.TenantId, c.EntryKind, c.EntityId, c.Reason, c.FactOccurredAt })
            .HasDatabaseName("IX_ReconciliationClosures_Tenant_Entry_Reason_Fact");
    }
}
