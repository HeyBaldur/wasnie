using Wasnie.Application.Common.Abstractions;

namespace Wasnie.Infrastructure.Persistence;

/// <summary>
/// La marca de «esto es onboarding», viva durante una petición. Ver <see cref="ISandboxScope"/>.
/// </summary>
public sealed class SandboxScope : ISandboxScope
{
    public bool IsSandbox { get; private set; }

    public void Enter() => IsSandbox = true;
}
