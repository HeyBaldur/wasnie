using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Assistant.Abstractions;

namespace Wasnie.Infrastructure.Services.Assistant;

/// <summary>
/// The documentation, read from the file that ships beside the binary and split by its headings.
///
/// ★ HOW IT STAYS CURRENT, which is the part worth getting right: the file is not a copy. The
/// Infrastructure project links `docs/Wasnie_Configuration_Guide.md` — the SAME file the team edits and
/// publishes the handbook from — and copies it to the output on build, so it reaches every project that
/// references Infrastructure from one declaration. Updating what the assistant knows is editing that
/// document; no code changes, no second copy to keep in sync. A duplicated corpus would drift from the
/// guide within a release, and an assistant confidently quoting last month's rules is worse than one
/// that says it does not know.
///
/// Read and split once, then cached: the file cannot change while the process runs.
/// </summary>
public sealed class FileAssistantKnowledgeBase : IAssistantKnowledgeBase
{
    /// <summary>Where the linked document lands in the output directory (see Wasnie.Infrastructure.csproj).</summary>
    public const string RelativePath = "Knowledge/Wasnie_Configuration_Guide.md";

    /// <summary>Splits before each `## ` heading, keeping the heading with its body.</summary>
    private static readonly Regex SectionSplit = new(@"(?m)^(?=## )", RegexOptions.Compiled);

    private readonly Lazy<Corpus> _corpus;

    public FileAssistantKnowledgeBase(ILogger<FileAssistantKnowledgeBase> logger)
    {
        _corpus = new Lazy<Corpus>(() => Load(logger));
    }

    public string Documentation => _corpus.Value.Text;

    public bool IsAvailable => Documentation.Length > 0;

    public IReadOnlyList<DocumentationSection> Sections => _corpus.Value.Sections;

    public string TableOfContents => _corpus.Value.TableOfContents;

    public string TextFor(IEnumerable<string> sectionIds)
    {
        var wanted = new HashSet<string>(
            (sectionIds ?? []).Select(Normalise).Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        // Iterating the SECTIONS, not the requested ids, is what puts the output in document order and
        // silently drops anything the router invented.
        foreach (var section in Sections.Where(s => wanted.Contains(s.Id)))
        {
            builder.Append(section.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Accepts what a model actually returns, not only what it was told to return.
    ///
    /// ★ THIS COST A RELEASE. The router's prompt showed an example of `["3", "15"]` — written before
    /// the ids gained their `s` prefix and never updated with them. The model copied the example
    /// faithfully, every id missed on an exact match, every question fell through to "I do not have
    /// that in the documentation", and the routing had actually been CORRECT the whole time. The
    /// example is fixed; this is the belt to that pair of braces, because a model will occasionally
    /// drop a prefix no matter how clearly it is asked not to.
    ///
    /// Deliberately narrow: it recovers a missing prefix and surrounding punctuation, and nothing else.
    /// Guessing harder than that would start matching sections the router did not choose.
    /// </summary>
    private static string Normalise(string? id)
    {
        var trimmed = (id ?? string.Empty).Trim().Trim('"', '\'', '.', ',', '#');

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // "6" → "s6". A bare number can only have meant the id, since no section title is an id.
        return trimmed.All(char.IsDigit) ? $"s{trimmed}" : trimmed;
    }

    private sealed record Corpus(string Text, IReadOnlyList<DocumentationSection> Sections, string TableOfContents);

    private static Corpus Load(ILogger logger)
    {
        var path = Path.Combine(AppContext.BaseDirectory, RelativePath);

        string text;
        try
        {
            if (!File.Exists(path))
            {
                // Degraded, not fatal: the assistant answers unanchored rather than refusing to work.
                // A warning because it IS a real loss — answers stop being grounded in Wasnie.
                logger.LogWarning(
                    "Assistant documentation not found at {Path}; the assistant will answer without it.",
                    path);
                return new Corpus(string.Empty, [], string.Empty);
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Assistant documentation could not be read from {Path}.", path);
            return new Corpus(string.Empty, [], string.Empty);
        }

        var sections = Split(text);

        logger.LogInformation(
            "Assistant documentation loaded: {Characters} characters, {Sections} sections.",
            text.Length, sections.Count);

        return new Corpus(text, sections, BuildTableOfContents(sections));
    }

    private static IReadOnlyList<DocumentationSection> Split(string text)
    {
        var parts = SectionSplit.Split(text);
        var sections = new List<DocumentationSection>(parts.Length);

        // parts[0] is the preamble before the first heading — the document's title and framing. It is
        // NOT a routable section: it answers nothing, and offering it would let the router spend the
        // budget on it.
        for (var i = 1; i < parts.Length; i++)
        {
            var body = parts[i];
            var heading = body.Split('\n', 2)[0].TrimStart('#', ' ').Trim();

            // Sequential ids rather than slugs of the titles: short (so the table of contents stays
            // cheap), and immune to a heading being reworded — which happens to documentation.
            //
            // ★ PREFIXED WITH 's' so the id can never be confused with the guide's OWN numbering. Bare
            // integers produced a table of contents reading "17: 15. Clawback" — two different numbers
            // for one section, in the exact place a human reads a log to work out what the router chose.
            sections.Add(new DocumentationSection($"s{i}", heading, body));
        }

        return sections;
    }

    private static string BuildTableOfContents(IReadOnlyList<DocumentationSection> sections)
    {
        var builder = new StringBuilder();

        foreach (var section in sections)
        {
            builder.Append(section.Id).Append(": ").AppendLine(section.Title);
        }

        return builder.ToString();
    }
}
