using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Payees;

namespace Wasnie.Infrastructure.Persistence.Configurations;

public sealed class PayeeConfiguration : IEntityTypeConfiguration<Payee>
{
    public void Configure(EntityTypeBuilder<Payee> builder)
    {
        builder.ToTable("Payees");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.FullName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.EmployeeCode).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Email).HasMaxLength(255);
        builder.Property(p => p.Role).HasMaxLength(100);
        builder.Property(p => p.ManagerId);
        builder.Property(p => p.HireDate);
        builder.Property(p => p.TerminationDate);
        builder.Property(p => p.Status).HasConversion<int>().IsRequired();
        builder.Property(p => p.EmploymentType).HasConversion<int?>();
        builder.Property(p => p.Location).HasMaxLength(200);
        builder.Property(p => p.TenantId).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.EmployeeCode }).IsUnique();

        // Filtered unique index: allows multiple rows with null email (per Decision D + WI-PROD-MODEL)
        builder.HasIndex(p => new { p.TenantId, p.Email })
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL");

        builder.HasIndex(p => p.ManagerId);
        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.FullName });

        builder.HasOne<Payee>()
            .WithMany()
            .HasForeignKey(p => p.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
