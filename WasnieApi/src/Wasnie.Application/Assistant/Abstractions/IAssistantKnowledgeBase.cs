namespace Wasnie.Application.Assistant.Abstractions;

/// <summary>One heading of the documentation and everything under it.</summary>
/// <param name="Id">Stable within a build; what the router names when it picks a section.</param>
public sealed record DocumentationSection(string Id, string Title, string Text);

/// <summary>
/// The product documentation the assistant answers from, addressable by section.
///
/// ★ WHY SECTIONS AND NOT THE WHOLE THING. The first attempt sent the entire guide — about fifteen
/// thousand tokens — reasoning that it fits a hundred-and-twenty-eight-thousand-token context. It does.
/// It also exceeded Groq's TOKENS-PER-MINUTE allowance, which the API applies per request and refuses
/// with HTTP 413, so not a single question ever got through. The corpus has to arrive in pieces, and
/// something has to choose which pieces.
///
/// ★ STILL NOT A VECTOR STORE. The choosing is done by the model itself, from a table of contents
/// (see AssistantSectionRouter) — twenty-one headings, one small call. Embeddings and an index earn
/// their cost on a corpus too large to describe in a prompt; a list of twenty-one titles is not that.
/// If the guide ever becomes a library, this interface is the seam where real retrieval goes.
/// </summary>
public interface IAssistantKnowledgeBase
{
    /// <summary>
    /// The whole document as text, or empty when it cannot be read.
    ///
    /// Empty is a degraded state, not a crash: the assistant falls back to a prompt that admits it has
    /// no source. A missing file must not take the chat down with it.
    /// </summary>
    string Documentation { get; }

    /// <summary>False when the documentation could not be loaded — the assistant is then unanchored.</summary>
    bool IsAvailable { get; }

    /// <summary>Every section, in document order.</summary>
    IReadOnlyList<DocumentationSection> Sections { get; }

    /// <summary>
    /// The table of contents the router reads: one `id: title` per line. A few hundred tokens, which
    /// is the entire input cost of deciding what the second call should carry.
    /// </summary>
    string TableOfContents { get; }

    /// <summary>
    /// The text of the named sections, in DOCUMENT order — never in the order the router named them.
    /// The guide reads forward, and handing the model section 15 before section 4 invites it to
    /// describe a later rule as if it came first. Unknown ids are ignored.
    /// </summary>
    string TextFor(IEnumerable<string> sectionIds);
}
