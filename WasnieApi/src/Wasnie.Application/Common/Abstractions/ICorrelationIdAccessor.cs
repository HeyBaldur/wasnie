namespace Wasnie.Application.Common.Abstractions;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
