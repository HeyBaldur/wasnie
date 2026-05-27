using Microsoft.AspNetCore.Http;
using Wasnie.Application.Common.Abstractions;

namespace Wasnie.Infrastructure.Observability;

public sealed class CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor) : ICorrelationIdAccessor
{
    public string? CorrelationId => httpContextAccessor.HttpContext?.Items["CorrelationId"] as string;
}
