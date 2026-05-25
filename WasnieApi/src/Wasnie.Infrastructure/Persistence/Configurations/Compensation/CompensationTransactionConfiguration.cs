using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class CompensationTransactionConfiguration : IEntityTypeConfiguration<CompensationTransaction>
{
    public void Configure(EntityTypeBuilder<CompensationTransaction> builder)
    {
        builder.ToTable("CompensationTransactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.ReferenceNumber).IsRequired().HasMaxLength(200);
        builder.Property(t => t.PayeeId).IsRequired();
        builder.Property(t => t.TransactionDate).IsRequired();
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(t => t.ExternalReference).HasMaxLength(500);
        builder.Property(t => t.IngestedAt).IsRequired();
        builder.Property(t => t.IngestedBy).IsRequired().HasMaxLength(450);
        builder.Property(t => t.UpdatedAt).IsRequired();

        builder.OwnsOne(t => t.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("Amount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(t => new { t.TenantId, t.ReferenceNumber }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.PayeeId });
    }
}
