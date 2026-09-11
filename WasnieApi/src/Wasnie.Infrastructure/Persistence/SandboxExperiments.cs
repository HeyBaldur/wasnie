using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Sandbox;

namespace Wasnie.Infrastructure.Persistence;

/// <summary>El historial de experimentos. Ver <see cref="ISandboxExperiments"/>.</summary>
public sealed class SandboxExperiments(
    SandboxDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IGuidGenerator guid,
    IClock clock)
    : ISandboxExperiments
{
    private string UserId => currentUser.UserId ?? string.Empty;

    public async Task<IReadOnlyList<SandboxExperimentDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Experiments
            .AsNoTracking()
            .Where(e => e.UserId == UserId)
            .OrderByDescending(e => e.UpdatedAt)
            .Select(e => new SandboxExperimentDto(e.Id, e.Name, e.Snapshot, e.CreatedAt, e.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<SandboxExperimentDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Experiments
            .AsNoTracking()
            .Where(e => e.Id == id && e.UserId == UserId)
            .Select(e => new SandboxExperimentDto(e.Id, e.Name, e.Snapshot, e.CreatedAt, e.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Crea uno nuevo, o vuelve a guardar sobre el que se pasa.
    /// </summary>
    /// <remarks>
    /// ★ EL DUEÑO SE COMPRUEBA AL ACTUALIZAR, no sólo al listar. Un identificador de otra persona no
    /// da error de permiso ni sobrescribe nada: se comporta como si no existiera, que es la misma
    /// respuesta que da el resto del producto cuando algo no es tuyo.
    /// </remarks>
    public async Task<SandboxExperimentDto> SaveAsync(
        Guid? id, string name, string snapshot, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset;

        var existing = id is null
            ? null
            : await db.Experiments.FirstOrDefaultAsync(
                e => e.Id == id.Value && e.UserId == UserId, cancellationToken);

        if (existing is null)
        {
            existing = SandboxExperiment.Create(
                guid.NewGuid(), tenantContext.TenantId, UserId, name, snapshot, now);
            db.Experiments.Add(existing);
        }
        else
        {
            existing.Update(name, snapshot, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new SandboxExperimentDto(
            existing.Id, existing.Name, existing.Snapshot, existing.CreatedAt, existing.UpdatedAt);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await db.Experiments
            .Where(e => e.Id == id && e.UserId == UserId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }
}
