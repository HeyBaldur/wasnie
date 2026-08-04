using System.Text.RegularExpressions;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Turns the first thing a user says into the name of the thread.
///
/// ★ WHY NOT A TIMESTAMP, which is what this replaced. "Chat 2026-07-31 14:58" tells the reader
/// nothing they can use: a history list of a dozen of those is a list you have to open one by one.
/// The first message is what people actually remember a conversation by, which is why every chat
/// product titles them this way.
///
/// ★ AND WHY NOT ASK THE MODEL. A second call per conversation, spending the tokens-per-day budget
/// that is already the binding constraint, to summarise a sentence the user just wrote — when the
/// sentence itself is the better title. If summarising ever becomes worth it, this is the one place
/// it would go.
/// </summary>
public static class ConversationTitle
{
    /// <summary>
    /// Long enough to be recognisable, short enough for a 420px panel without wrapping to three lines.
    /// </summary>
    public const int MaxLength = 60;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Markdown a user might type. Stripped rather than rendered, because a title is one line of plain
    /// text in a list — `**urgent**` should read as "urgent", not carry its asterisks around forever.
    /// </summary>
    private static readonly Regex MarkdownNoise = new(@"[*_`~#>\[\]]", RegexOptions.Compiled);

    public static string FromMessage(string? message)
    {
        var collapsed = Whitespace.Replace(MarkdownNoise.Replace(message ?? string.Empty, string.Empty), " ").Trim();

        if (collapsed.Length == 0)
        {
            return string.Empty;
        }

        if (collapsed.Length <= MaxLength)
        {
            return collapsed;
        }

        // Cut at a word boundary when there is one nearby, so a title does not end mid-word. The
        // threshold stops a single very long token from collapsing the title to almost nothing.
        var truncated = collapsed[..MaxLength];
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > MaxLength / 2)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated.TrimEnd() + "…";
    }
}
