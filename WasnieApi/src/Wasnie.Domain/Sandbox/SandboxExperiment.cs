using Wasnie.Domain.Common;

namespace Wasnie.Domain.Sandbox;

/// <summary>
/// Un experimento del sandbox: la configuración que alguien probó, con su nombre y su fecha.
/// </summary>
/// <remarks>
/// ★★ ES UNA FOTO, NO UN CONTENEDOR DE FILAS, Y ESA ES LA DECISIÓN DE DISEÑO CENTRAL. La alternativa
/// natural —que cada plan, regla y venta del sandbox llevara un `ExperimentId`— es imposible sin
/// romper lo que hace funcionar todo esto: las entidades del sandbox son LAS MISMAS CLASES que las
/// reales, y añadirles una columna de experimento la añadiría también a las tablas del dinero de
/// verdad. La foto guarda lo que el usuario configuró y lo que le dio, sin tocar ni un campo del
/// modelo compartido.
///
/// ★ Y ES POR USUARIO. Dos personas de la misma empresa prueban cosas distintas; el historial de una
/// no es el de la otra.
/// </remarks>
public sealed class SandboxExperiment : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>Quién lo guardó. El historial es personal.</summary>
    public string UserId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// La configuración y el resultado, serializados.
    ///
    /// ★ TEXTO Y NO COLUMNAS, A PROPÓSITO: lo que se guarda es lo que la pantalla enseñó, y esa forma
    /// cambiará cuando cambien los pasos. Un esquema rígido obligaría a una migración por cada campo
    /// nuevo del recorrido, para un dato que sólo se lee para volver a mirarlo.
    /// </summary>
    public string Snapshot { get; private set; } = "{}";

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SandboxExperiment() { }

    public static SandboxExperiment Create(
        Guid id, Guid tenantId, string userId, string name, string snapshot, DateTimeOffset now) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(name) ? "Experiment" : name.Trim(),
            Snapshot = snapshot,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>Renombrar o volver a guardar sobre el mismo experimento.</summary>
    public void Update(string name, string snapshot, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        Snapshot = snapshot;
        UpdatedAt = now;
    }
}
