namespace Wasnie.Application.Common.Interfaces;

public interface ITierLimitChecker
{
    Task EnsurePayeeLimitAsync(CancellationToken cancellationToken = default);
    Task EnsurePlanLimitAsync(CancellationToken cancellationToken = default);
}
