using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Ledger;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class PayeeBalanceConfiguration : IEntityTypeConfiguration<PayeeBalance>
{
    public void Configure(EntityTypeBuilder<PayeeBalance> builder)
    {
        builder.ToTable("PayeeBalances");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.PayeeId).IsRequired();
        builder.Property(b => b.Currency).IsRequired().HasMaxLength(3);
        builder.Property(b => b.UpdatedAt).IsRequired();

        builder.OwnsOne(b => b.Balance, m =>
        {
            m.Property(x => x.Amount).HasColumnName("Balance").HasColumnType("decimal(18,4)").IsRequired();
            // The Money currency is the same value as the Currency column above; mapping it to its own
            // column keeps the owned type self-contained instead of depending on load order.
            m.Property(x => x.Currency).HasColumnName("BalanceCurrency").HasMaxLength(3).IsRequired();
        });

        // Same OCC mechanism as Credit.RowVersion (CreditConfiguration.cs) — a real SQL rowversion,
        // not a shadow property. This is what makes a manual adjustment landing mid-settlement fail
        // loudly instead of being overwritten by a balance computed from stale data.
        builder.Property(b => b.RowVersion).IsRowVersion();

        // Exactly ONE balance row per payee and currency. Without this a race on first-touch could
        // create two rows and each writer would net against half the debt.
        builder.HasIndex(b => new { b.TenantId, b.PayeeId, b.Currency })
            .IsUnique()
            .HasDatabaseName("UX_PayeeBalances_Tenant_Payee_Currency");
    }
}
