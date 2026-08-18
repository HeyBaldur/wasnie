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
        // 450 = the ASP.NET Identity key length, so the column can hold any IdentityUser.Id verbatim.
        builder.Property(p => p.UserId).HasMaxLength(450);
        builder.Property(p => p.ManagerId);
        builder.Property(p => p.HireDate);
        builder.Property(p => p.TerminationDate);
        builder.Property(p => p.Status).HasConversion<int>().IsRequired();
        builder.Property(p => p.EmploymentType).HasConversion<int?>();
        builder.Property(p => p.Location).HasMaxLength(200);
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.DeactivatedAt);
        builder.Property(p => p.TenantId).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.EmployeeCode }).IsUnique();

        // Filtered unique index: allows multiple rows with null email (per Decision D + WI-PROD-MODEL)
        builder.HasIndex(p => new { p.TenantId, p.Email })
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL");

        builder.HasIndex(p => p.ManagerId);

        // The authorisation lookup runs on EVERY read of a payee's ledger, so it gets its own index.
        // Filtered because unlinked payees are never looked up by user — and a plain unique index would
        // collide on all of them. Unique: one user owns at most one payee per tenant, which is what
        // makes "my balance" a single answer instead of a list.
        builder.HasIndex(p => new { p.TenantId, p.UserId })
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("UX_Payees_Tenant_UserId");
        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.FullName });

        builder.HasOne<Payee>()
            .WithMany()
            .HasForeignKey(p => p.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
