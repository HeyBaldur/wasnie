using MediatR;

namespace Wasnie.IntegrationTests.Infrastructure;

/// <summary>
/// Shared no-op MediatR publisher for use in service-level tests that need
/// to construct ApplicationDbContext without a real DI container.
/// </summary>
internal sealed class NoOpPublisher : IPublisher
{
    public static readonly NoOpPublisher Instance = new();

    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
        => Task.CompletedTask;
}
