namespace Wasnie.Application.Common.Abstractions;

/// <summary>
/// Borra todo lo que la empresa haya creado practicando en el onboarding.
///
/// ★★ ES UNA OPERACIÓN PROPIA, NO UN BORRADO GENÉRICO CON UNA BANDERA. Quien la implementa habla
/// SIEMPRE con el contexto del sandbox, nunca con la interfaz conmutable: así no existe la variante
/// «se ejecutó sin entrar al sandbox» y este método no puede, ni por error de configuración, borrar
/// dinero real. El nombre dice lo que hace y el tipo garantiza dónde lo hace.
/// </summary>
public interface ISandboxReset
{
    /// <summary>Vacía los datos de práctica del tenant actual. Devuelve cuántas filas se borraron.</summary>
    Task<int> ResetAsync(CancellationToken cancellationToken);
}
