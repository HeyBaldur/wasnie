using Wasnie.Application.Common.Abstractions;

namespace Wasnie.IntegrationTests.TestDoubles;

public sealed class FakeGuidGenerator : IGuidGenerator
{
    private readonly Queue<Guid> _queue = new();

    public void Enqueue(params Guid[] guids)
    {
        foreach (var g in guids) _queue.Enqueue(g);
    }

    public Guid NewGuid() => _queue.Count > 0 ? _queue.Dequeue() : Guid.NewGuid();
}
