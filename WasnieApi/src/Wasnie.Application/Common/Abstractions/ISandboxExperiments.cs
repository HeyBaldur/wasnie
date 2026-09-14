namespace Wasnie.Application.Common.Abstractions;

/// <param name="Snapshot">La foto del experimento, tal como la guardó la pantalla.</param>
public sealed record SandboxExperimentDto(
    Guid Id,
    string Name,
    string Snapshot,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// El historial de experimentos del sandbox: crear, listar, abrir, renombrar y borrar.
/// </summary>
/// <remarks>
/// ★ HABLA SIEMPRE CON EL CONTEXTO DEL SANDBOX, como el reseteo. No es una operación que pueda
/// ejecutarse «sin querer» contra los datos reales: el tipo garantiza dónde escribe.
///
/// ★ Y ES POR USUARIO. Filtra por la persona, no sólo por la empresa: el historial de pruebas de uno
/// no es el de su compañero.
/// </remarks>
public interface ISandboxExperiments
{
    Task<IReadOnlyList<SandboxExperimentDto>> ListAsync(CancellationToken cancellationToken);

    Task<SandboxExperimentDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<SandboxExperimentDto> SaveAsync(Guid? id, string name, string snapshot, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
