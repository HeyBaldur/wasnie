namespace Wasnie.Application.Common.Abstractions;

/// <summary>
/// Dice si lo que se está ejecutando en esta petición es el onboarding guiado o el producto de verdad.
///
/// ★★ SE DECLARA, NO SE ADIVINA. Lo pone explícitamente el controlador del onboarding antes de
/// despachar el comando; no se deduce de una cabecera, de un trozo de la URL ni de un rol. Deducirlo
/// sería una regla que un día cambia de sitio y deja escribiendo dinero de prueba en las tablas
/// reales, que es exactamente lo que este diseño existe para hacer imposible.
///
/// ★ ES DE IDA, NO DE VUELTA. Dentro de una petición se puede entrar al sandbox y nunca salir: una
/// unidad de trabajo que empezara real y terminara de prueba (o al revés) dejaría media escritura a
/// cada lado.
/// </summary>
public interface ISandboxScope
{
    bool IsSandbox { get; }

    /// <summary>Marca esta petición como del onboarding. Idempotente.</summary>
    void Enter();
}
