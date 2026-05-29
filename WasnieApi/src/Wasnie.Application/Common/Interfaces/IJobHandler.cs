using Wasnie.Application.Common.Models;

namespace Wasnie.Application.Common.Interfaces;

public interface IJobHandlerBase
{
    Task ExecuteRawAsync(string payloadJson, JobContext context, CancellationToken ct);
}

public interface IJobHandler<TPayload> : IJobHandlerBase
{
    Task HandleAsync(TPayload payload, JobContext context, CancellationToken ct);
}
