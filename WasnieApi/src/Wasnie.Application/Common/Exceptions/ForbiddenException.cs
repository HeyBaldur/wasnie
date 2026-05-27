namespace Wasnie.Application.Common.Exceptions;

public sealed class ForbiddenException(string permission)
    : Exception($"Permission denied: {permission}")
{
    public string Permission { get; } = permission;
}
