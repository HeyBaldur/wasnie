using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Sandbox;

namespace Wasnie.Infrastructure.Persistence.Configurations;

/// <summary>
/// El historial de experimentos. Se aplica ÚNICAMENTE en el contexto del sandbox: ver la nota de
/// <see cref="ApplicationDbContext"/> — esta tabla no existe en el esquema real y no debe existir.
/// </summary>
public sealed class SandboxExperimentConfiguration : IEntityTypeConfiguration<SandboxExperiment>
{
    public void Configure(EntityTypeBuilder<SandboxExperiment> builder)
    {
        builder.ToTable("Experiments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Snapshot).IsRequired();

        // El historial se lee siempre por empresa y persona, y siempre por fecha.
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.UpdatedAt });
    }
}
