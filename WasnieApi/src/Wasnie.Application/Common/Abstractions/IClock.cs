namespace Wasnie.Application.Common.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTimeOffset UtcNowOffset { get; }
}
