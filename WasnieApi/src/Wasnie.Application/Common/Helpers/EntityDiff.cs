using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wasnie.Application.Common.Helpers;

public static class EntityDiff
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string? Serialize(object? obj) =>
        obj is null ? null : JsonSerializer.Serialize(obj, Options);
}
