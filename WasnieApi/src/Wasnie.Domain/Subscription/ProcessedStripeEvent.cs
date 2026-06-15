namespace Wasnie.Domain.Subscription;

public sealed class ProcessedStripeEvent
{
    public string EventId { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedStripeEvent() { }

    public static ProcessedStripeEvent Create(string eventId, DateTimeOffset processedAt) =>
        new() { EventId = eventId, ProcessedAt = processedAt };
}
