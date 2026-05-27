using Wasnie.Application.Common.Abstractions;

namespace Wasnie.IntegrationTests.TestDoubles;

public sealed class FakeClock(DateTime? initial = null) : IClock
{
    private DateTime _utcNow = initial ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DateTime UtcNow => _utcNow;
    public DateTimeOffset UtcNowOffset => new(_utcNow);

    public void Set(DateTime utcNow) => _utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
