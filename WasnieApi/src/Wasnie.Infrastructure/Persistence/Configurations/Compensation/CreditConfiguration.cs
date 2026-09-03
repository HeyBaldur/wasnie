using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class CreditConfiguration : IEntityTypeConfiguration<Credit>
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        opts.Converters.Add(new RuleSnapshotJsonConverter());
        return opts;
    }

    public void Configure(EntityTypeBuilder<Credit> builder)
    {
        builder.ToTable("Credits");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.TransactionId).IsRequired();
        builder.Property(c => c.PayeeId).IsRequired();
        builder.Property(c => c.PlanId).IsRequired();
        builder.Property(c => c.RuleId).IsRequired();
        builder.Property(c => c.Role).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(c => c.AllocatedAt).IsRequired();
        builder.Property(c => c.AllocatedBy).IsRequired().HasMaxLength(450);
        builder.Property(c => c.SupersededAt).IsRequired(false);
        builder.Property(c => c.SupersededBy).HasColumnType("nvarchar(max)").IsRequired(false);
        builder.Property(c => c.ConsumedAt).IsRequired(false);
        builder.Property(c => c.ConsumedByPayoutId).IsRequired(false);

        // ── Closed without ever being paid ────────────────────────────────────────────────────────
        // The reason is stored AS A STRING, like Role above: an int would make every closure report a
        // join against a C# enum nobody can read in SSMS, and "WrittenOff" in a row is the difference
        // between a finance question answered in one query and one answered by a developer.
        builder.Property(c => c.ClosedAt).IsRequired(false);
        builder.Property(c => c.ClosedBy).HasMaxLength(450).IsRequired(false);
        builder.Property(c => c.ClosureReason).HasConversion<string?>().HasMaxLength(50).IsRequired(false);
        builder.Property(c => c.ClosureNote).HasMaxLength(1000).IsRequired(false);

        // ★ THE INDEX THE ENGINE NEEDS. Every pay run now filters on the three nulls together
        // (SupersededAt, ConsumedAt, ClosedAt) for one payee and plan; the orphan queue asks the same
        // question for a set of payees. A filtered index on the outstanding rows keeps both cheap as
        // the closed ones accumulate — they are terminal, so that pile only ever grows.
        builder.HasIndex(c => new { c.TenantId, c.PayeeId })
            .HasDatabaseName("IX_Credits_TenantId_PayeeId_Outstanding")
            .HasFilter("[SupersededAt] IS NULL AND [ConsumedAt] IS NULL AND [ClosedAt] IS NULL");

        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.OwnsOne(c => c.OriginalAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("OriginalAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("OriginalCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(c => c.CreditedAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("CreditedAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("CreditedCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(c => c.SplitPercentage, pct =>
        {
            pct.Property(x => x.Value).HasColumnName("SplitPercentage").HasColumnType("decimal(5,4)").IsRequired();
        });

        // The calculation trace: an opaque JSON document, stored as text and never queried by SQL.
        // No value converter, because there is nothing to convert — the property already IS the
        // document. CalculationTraceSerializer owns the shape on both sides.
        //
        // Nullable with no default, which is what makes this migration inert: every existing credit
        // reads back null, meaning "we did not record this", and nothing has to be backfilled — the
        // inputs to reconstruct those traces are gone, so a backfill could only invent them.
        builder.Property(c => c.CalculationTrace)
            .HasColumnType("nvarchar(max)")
            .IsRequired(false);

        // The one fact out of that document that IS queried by SQL: why the rate component refused.
        // Short and indexed, because the reconciliation queue filters and groups by it over every
        // credit in a tenant — the alternative was scanning the nvarchar(max) above on every page and
        // every aggregate, and making its internal field names a query contract.
        //
        // Filtered index: refusals are the rare case (a healthy tenant has none), so indexing only
        // the non-null rows keeps it a fraction of the table instead of a full-width copy of it.
        builder.Property(c => c.RateRefusal)
            .HasMaxLength(64)
            .IsRequired(false);

        builder.HasIndex(c => new { c.TenantId, c.RateRefusal })
            .HasFilter("[RateRefusal] IS NOT NULL")
            .HasDatabaseName("IX_Credits_TenantId_RateRefusal");

        builder.Property(c => c.RuleSnapshot)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<RuleSnapshot>(v, JsonOptions)!);

        builder.HasIndex(c => new { c.TenantId, c.TransactionId, c.PayeeId });

        // ── Anti-double-pay, DECLARATIVE ──────────────────────────────────────────────────────
        // One live Credit per (transaction, plan, rule). Until now the only thing preventing a
        // duplicate credit was procedural — the batch guard in ProcessPendingTransactionsJobHandler
        // that skips transactions which already have credits. Nothing in the database enforced it, so
        // a wrong key in that guard would have produced duplicate credits SILENTLY, and they would
        // have been paid. This index is the net underneath that guard.
        //
        // Filtered to non-superseded rows on purpose: RecalculateCredits supersedes a credit and
        // creates its replacement with the SAME (transaction, plan, rule), so superseded rows must be
        // exempt or every recalculation would fail. A CONSUMED (already paid) credit is still live for
        // this purpose — that is precisely the row a duplicate must never be created against.
        //
        // PlanId is functionally implied by RuleId (Rule.cs:11 — a rule belongs to exactly one plan,
        // verified in the data: no RuleId spans two plans). It is kept in the key so the constraint
        // reads as the business rule it encodes, and so the plan dimension stays explicit as the
        // engine moves toward crediting several plans per transaction.
        builder.HasIndex(c => new { c.TenantId, c.TransactionId, c.PlanId, c.RuleId })
            .IsUnique()
            .HasFilter("[SupersededAt] IS NULL")
            .HasDatabaseName("UX_Credits_Tenant_Transaction_Plan_Rule_Live");
        builder.HasIndex(c => new { c.TenantId, c.SupersededAt })
            .HasFilter("[SupersededAt] IS NULL");
        builder.HasIndex(c => new { c.TenantId, c.ConsumedAt })
            .HasFilter("[ConsumedAt] IS NULL");
        builder.HasIndex(c => c.ConsumedByPayoutId)
            .HasFilter("[ConsumedByPayoutId] IS NOT NULL");
    }
}
