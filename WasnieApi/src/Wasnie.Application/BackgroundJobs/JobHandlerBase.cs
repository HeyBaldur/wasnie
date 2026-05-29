using System.Text.Json;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;

namespace Wasnie.Application.BackgroundJobs;

public abstract class JobHandlerBase<TPayload> : IJobHandler<TPayload>
{
    public Task ExecuteRawAsync(string payloadJson, JobContext context, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<TPayload>(payloadJson)
            ?? throw new InvalidOperationException($"Failed to deserialize payload as {typeof(TPayload).Name}.");
        return HandleAsync(payload, context, ct);
    }

    public abstract Task HandleAsync(TPayload payload, JobContext context, CancellationToken ct);
}
