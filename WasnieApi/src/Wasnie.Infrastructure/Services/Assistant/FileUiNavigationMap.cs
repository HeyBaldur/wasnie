using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;

namespace Wasnie.Infrastructure.Services.Assistant;

/// <summary>
/// The navigation map, read from the JSON that ships beside the binary.
///
/// LINKED, NOT COPIED, exactly like the documentation (see Wasnie.Infrastructure.csproj): the file the
/// team curates IS the file the assistant reads. A duplicate would drift, and an assistant handing out
/// last release's URLs is the failure this map exists to prevent.
///
/// Read and rendered once, then cached: the file cannot change while the process runs.
/// </summary>
public sealed class FileUiNavigationMap : IUiNavigationMap
{
    /// <summary>Where the linked document lands in the output directory (see Wasnie.Infrastructure.csproj).</summary>
    public const string RelativePath = "Knowledge/UINavigationMap.json";

    private readonly Lazy<Map> _map;

    public FileUiNavigationMap(ILogger<FileUiNavigationMap> logger)
    {
        _map = new Lazy<Map>(() => Load(logger));
    }

    public string PromptBlock => _map.Value.PromptBlock;

    public bool IsAvailable => PromptBlock.Length > 0;

    public IReadOnlyList<string> Routes => _map.Value.Routes;

    private sealed record Map(string PromptBlock, IReadOnlyList<string> Routes);

    private static Map Load(ILogger logger)
    {
        var path = Path.Combine(AppContext.BaseDirectory, RelativePath);

        MapFile? parsed;
        try
        {
            if (!File.Exists(path))
            {
                // Degraded, not fatal — same posture as the missing documentation. The assistant keeps
                // answering; it just has no routes to offer, and the prompt rule turns that into
                // "named screen, no link" rather than into a guess.
                logger.LogWarning(
                    "Assistant navigation map not found at {Path}; the assistant will guide without links.",
                    path);
                return new Map(string.Empty, []);
            }

            parsed = JsonSerializer.Deserialize<MapFile>(File.ReadAllText(path), SerializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Assistant navigation map could not be read from {Path}.", path);
            return new Map(string.Empty, []);
        }

        var entries = parsed?.Entries ?? [];
        var unrouted = parsed?.ActionsWithoutRoute ?? [];

        if (entries.Count == 0)
        {
            logger.LogWarning("Assistant navigation map at {Path} declares no entries.", path);
            return new Map(string.Empty, []);
        }

        logger.LogInformation(
            "Assistant navigation map loaded: {Routes} routes, {Unrouted} screens without a direct route.",
            entries.Count, unrouted.Count);

        return new Map(Render(entries, unrouted), entries.Select(e => e.Route).ToList());
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// One line per screen, not the raw JSON.
    ///
    /// The file carries a long `_README` for the humans curating it; sending that to the model would
    /// spend tokens on instructions addressed to somebody else, and the braces and quotes of JSON are
    /// noise a model has to parse before it can use anything. A table of `route | label | purpose` is
    /// the same information in a fraction of the space.
    /// </summary>
    private static string Render(
        IReadOnlyList<MapEntry> entries,
        IReadOnlyList<UnroutedEntry> unrouted)
    {
        var builder = new StringBuilder();

        builder.AppendLine("These are the ONLY routes that exist. Format: route | button or screen name | what it is.");

        foreach (var entry in entries)
        {
            builder.Append(entry.Route).Append(" | ").Append(entry.Label).Append(" | ").AppendLine(entry.Purpose);
        }

        if (unrouted.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(
                "These screens have NO direct link. Name them and say how to reach them; do NOT invent a URL for them.");

            foreach (var entry in unrouted)
            {
                builder.Append(entry.Label).Append(" | reached from ").Append(entry.ReachedFrom)
                    .Append(" | ").AppendLine(entry.How);
            }
        }

        return builder.ToString();
    }

    private sealed record MapFile(
        [property: JsonPropertyName("entries")] IReadOnlyList<MapEntry> Entries,
        [property: JsonPropertyName("actionsWithoutRoute")] IReadOnlyList<UnroutedEntry> ActionsWithoutRoute);

    private sealed record MapEntry(string Intent, string Route, string Label, string Purpose);

    private sealed record UnroutedEntry(string Intent, string Label, string ReachedFrom, string How);
}
