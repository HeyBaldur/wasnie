using Wasnie.Application.Common.Abstractions;

namespace Wasnie.Infrastructure.Common;

public sealed class SystemGuidGenerator : IGuidGenerator
{
    public Guid NewGuid() => Guid.NewGuid();
}
