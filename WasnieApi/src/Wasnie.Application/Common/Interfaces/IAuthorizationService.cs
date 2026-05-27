namespace Wasnie.Application.Common.Interfaces;

public interface IAuthorizationService
{
    Task RequireAsync(string permission, CancellationToken cancellationToken = default);
}
