namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// The numbers the conversation list is paged and searched by, in one place so the handler, the
/// validator and the tests cannot disagree about them.
/// </summary>
public static class AssistantPaging
{
    /// <summary>
    /// How many conversations a batch holds when the caller does not say.
    ///
    /// Twenty-five is about two screens of the full-page rail and several of the drawer's dropdown, so
    /// the first batch always overfills the visible box — a "Load more" button that appears before the
    /// user has scrolled anything reads as the list being broken off early.
    /// </summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// The ceiling, enforced by the validator rather than clamped silently.
    ///
    /// ★ REJECTED, NOT TRIMMED. A caller asking for a thousand rows has misunderstood something, and
    /// quietly handing back a hundred lets that misunderstanding live: they page forward believing they
    /// have seen a thousand. An error says what happened at the moment it happened.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Shortest search worth running.
    ///
    /// ★ AND BELOW IT THE FILTER IS IGNORED, NOT REFUSED. One character matches most titles, so the
    /// "results" would be the list with extra latency and a misleading heading. The user is mid-word,
    /// not asking a question — so the ordinary list stays on screen and the search begins when it can
    /// mean something. Refusing instead would flash an error between the first and second keystroke of
    /// every search anyone ever types.
    /// </summary>
    public const int MinSearchLength = 2;

    /// <summary>
    /// The collation the title search compares under: case-insensitive AND accent-insensitive.
    ///
    /// ★ THE DATABASE'S DEFAULT IS NOT ENOUGH. SQL Server's usual default
    /// (SQL_Latin1_General_CP1_CI_AS) is case-insensitive but accent-SENSITIVE, so a user typing
    /// "asignacion" would not find "Asignación" — and titles here are written in Spanish, English and
    /// Polish by people who mostly do not reach for the accent key when searching. Stating the
    /// collation on the comparison makes the behaviour a property of this query rather than of whatever
    /// collation the server happened to be installed with.
    /// </summary>
    public const string SearchCollation = "Latin1_General_CI_AI";

    /// <summary>
    /// True when a search term is long enough to filter by. Trimmed first: a box holding only spaces is
    /// an empty box, and the user is not searching for whitespace.
    /// </summary>
    public static bool IsSearchable(string? term) =>
        (term ?? string.Empty).Trim().Length >= MinSearchLength;
}
