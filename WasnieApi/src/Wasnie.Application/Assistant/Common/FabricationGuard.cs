using System.Text;
using System.Text.RegularExpressions;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// WATCHES THE ANSWER BEING WRITTEN, AND CUTS IT OFF IF AN IDENTIFIER APPEARS THAT NO LOOKUP RETURNED.
///
/// ★★ WHAT HAPPENED (KAN-58). Asked which payouts two credits were in, the assistant produced
/// <c>U_9ae5725a…</c> and <c>U_10484470…</c> with complete confidence. Neither could exist: the credits
/// were Active/Superseded, consumed by no payout at all, and there is no payout lookup in the first
/// place. The <c>U_</c> prefix does not exist anywhere in Incentra — the format itself was invented.
/// The same answer also contained a CORRECT figure (5% of 61,216.23 = 3,060.81), and that is the part
/// that makes this dangerous rather than merely wrong: real data and invention, in one paragraph,
/// indistinguishable.
///
/// ★★ WHY A GUARD AND NOT ANOTHER PROMPT RULE. The prompt ALREADY forbids this, twice over and in the
/// strongest words it uses anywhere: rule 10b says "NEVER put an id in your answer, not in brackets,
/// not as a reference", and scenario 2D covers exactly "you cannot look at this at all". Both were in
/// place when the incident happened. Writing a sixth sentence about not inventing things is repeating
/// the mechanism that already failed. <see cref="DegenerationGuard"/> settled this argument for the
/// repetition collapse — "it is not something a prompt prevents" — and the reasoning transfers whole:
/// the prompt reduces the rate, the guard is what makes the user never read one.
///
/// ★★ THE RULE, AND IT IS A NEGATIVE ONE: an identifier-shaped token may appear in the answer ONLY if
/// it appears somewhere in what the model was GIVEN for this turn — the tool's JSON, the user's own
/// words, the conversation so far. Not "does it look real"; "did it come from somewhere". That is the
/// ticket's requirement stated as a computation: nothing that did not come from a real function call.
/// A model cannot fabricate a token that is already in its input, and it cannot smuggle one out that
/// is not.
///
/// ★ FAILING THE TURN IS THE RIGHT SEVERITY, and it is the same trade DegenerationGuard makes. A
/// partly-invented answer is not salvageable by trimming: the reader cannot tell which half to trust,
/// so leaving the rest on screen is worse than an error they can retry. Nothing is persisted (the
/// assistant row is written only after a clean finish), so the user keeps their question.
/// </summary>
public sealed class FabricationGuard
{
    /// <summary>
    /// The shortest run of hex that counts as identifier-shaped.
    ///
    /// ★ EIGHT, BECAUSE THAT IS A GUID'S FIRST GROUP and the observed fabrication was exactly that
    /// long (<c>9ae5725a</c>). Shorter runs are words and codes; a four-character one would fire on
    /// "cafe" and on half the hex-looking fragments of ordinary prose.
    /// </summary>
    private const int MinHexRun = 8;

    /// <summary>
    /// The boundary either side of an identifier-shaped token.
    ///
    /// ★★ NOT <c>\b</c>, AND THIS IS THE DETAIL THAT DECIDED WHETHER THIS CLASS WORKS AT ALL.
    /// <c>\b</c> sits between a word character and a non-word one, and UNDERSCORE IS A WORD CHARACTER —
    /// so in <c>U_9ae5725a</c>, the exact string this guard exists to catch, there is no boundary before
    /// the <c>9</c> and the token would not have matched. The prefix is what made the fabrication
    /// obvious to a human and invisible to the naive pattern.
    ///
    /// Excluding only letters and digits gets both halves right: <c>U_9ae5725a</c> matches on its hex
    /// run, while <c>abc9ae5725a</c> — a longer word that merely contains one — does not fragment into a
    /// false positive.
    /// </summary>
    private const string Boundary = @"(?<![0-9A-Za-z])";
    private const string BoundaryAfter = @"(?![0-9A-Za-z])";

    /// <summary>
    /// A canonical GUID, with its dashes.
    ///
    /// ★ MATCHED SEPARATELY FROM THE HEX RULE BELOW because a dashed GUID is not one token: splitting
    /// on punctuation would turn it into five short groups, four of which are under the length floor.
    /// (An undashed GUID is 32 hex characters and the hex rule catches it on its own.)
    /// </summary>
    private static readonly Regex GuidShaped = new(
        Boundary
        + @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
        + BoundaryAfter,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A run of hex long enough to be an identifier.
    ///
    /// ★★ IT MUST CONTAIN BOTH A DIGIT AND A HEX LETTER, and that pair of conditions is what keeps this
    /// guard off everything the product legitimately prints:
    ///
    ///   - A REFERENCE NUMBER is digits: <c>HUBSPOT-513636220111</c> has no hex letter, so it is never
    ///     flagged. Rule 10b explicitly permits quoting these back, and they are the single most common
    ///     string in a real answer — a guard that ate them would be unusable.
    ///   - AN AMOUNT is digits: <c>61216.23</c>, <c>3.060,81</c>. Never flagged.
    ///   - AN EMPLOYEE CODE is a word and digits: <c>EMP-001</c>, <c>DE-101</c>. No hex letter.
    ///   - AN ENGLISH WORD has no digit: <c>deadbeef</c>, <c>defaced</c>. Never flagged.
    ///
    /// So the only things left are things shaped like machine identifiers, which is the point.
    /// </summary>
    private static readonly Regex HexRunShaped = new(
        Boundary
        + @"(?=[0-9a-fA-F]*[0-9])(?=[0-9a-fA-F]*[a-fA-F])[0-9a-fA-F]{" + MinHexRun + @",}"
        + BoundaryAfter,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _allowed;
    private readonly StringBuilder _seen = new();

    /// <param name="grounding">
    /// Everything the model was given for this turn, concatenated: the tool's JSON payload, the user's
    /// message, and the conversation history in the prompt. Order and formatting do not matter — this
    /// is only ever asked "does this token occur in you?".
    ///
    /// ★ THE USER'S OWN WORDS BELONG IN HERE. Somebody who pastes an id and asks about it must be able
    /// to read it back in the reply — "I could not find anything matching 3f2a…" is a correct answer,
    /// and a guard that killed it would punish the one case where printing an identifier helps.
    /// </param>
    public FabricationGuard(string? grounding)
    {
        // Lower-cased once, because hex is case-insensitive and a model that echoes an id in a
        // different case has still echoed it rather than invented it.
        _allowed = (grounding ?? string.Empty).ToLowerInvariant();
    }

    /// <summary>True once a token was found that nothing gave the model.</summary>
    public bool IsFabricated { get; private set; }

    /// <summary>The offending token, for the operator's log. Never shown to the user.</summary>
    public string? Fabricated { get; private set; }

    /// <summary>
    /// Feeds one streamed fragment. Returns true when the answer must be abandoned.
    ///
    /// ★★ IT SCANS THE ACCUMULATED ANSWER, NOT THE FRAGMENT. A provider splits tokens wherever it
    /// likes: <c>9ae5725a</c> arrives as "9ae", "572", "5a" often enough that a per-fragment scan would
    /// have missed the very identifier this class is named after. The cost is re-scanning a tail, which
    /// is bounded below.
    /// </summary>
    public bool Observe(string? fragment)
    {
        if (IsFabricated) return true;
        if (string.IsNullOrEmpty(fragment)) return false;

        _seen.Append(fragment);

        // ★ ONLY THE TAIL IS RE-SCANNED. An identifier is short, so a window a few hundred characters
        // wide always contains any token still being written, and the scan stays O(1) per fragment
        // instead of O(answer length) — which on a 20,000-character ceiling is the difference between
        // free and quadratic.
        var window = Tail(1024);
        return Inspect(window);
    }

    /// <summary>
    /// The final check, over the WHOLE answer.
    ///
    /// ★ NOT REDUNDANT WITH <see cref="Observe"/>. The sliding window is a cheap early exit; this is the
    /// one that is complete. A guard that only ever looked at a tail would be a guard with a documented
    /// hole in it, and the hole would be in the middle of long answers — which are exactly the ones
    /// that mix real data with invention.
    /// </summary>
    public bool Finish() => !IsFabricated && Inspect(_seen.ToString());

    private string Tail(int size)
    {
        var length = _seen.Length;
        return length <= size ? _seen.ToString() : _seen.ToString(length - size, size);
    }

    private bool Inspect(string text)
    {
        foreach (Match match in GuidShaped.Matches(text))
        {
            if (Flag(match.Value)) return true;
        }

        foreach (Match match in HexRunShaped.Matches(text))
        {
            if (Flag(match.Value)) return true;
        }

        return false;
    }

    private bool Flag(string token)
    {
        if (_allowed.Contains(token.ToLowerInvariant(), StringComparison.Ordinal)) return false;

        IsFabricated = true;
        Fabricated = token;
        return true;
    }
}
