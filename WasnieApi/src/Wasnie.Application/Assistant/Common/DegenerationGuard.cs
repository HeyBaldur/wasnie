using System.Text;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// WATCHES THE ANSWER BEING WRITTEN, AND CUTS IT OFF IF THE MODEL STOPS MAKING SENSE.
///
/// ★ WHAT HAPPENED. Asked to explain a three-rule plan, gpt-oss-20b fell into a repetition loop
/// mid-sentence — "valor mandatorio mandatorio mandatorio mandatorio…" for hundreds of words — and
/// never reached the third rule. On screen. In a product whose entire subject is what people are paid.
///
/// ★ WHY THIS EXISTS EVEN THOUGH THE MODEL WAS UPGRADED. Raising the generation model is the fix;
/// this is the belt. Repetition collapse is a failure mode of language models in general, not of one
/// model, and it is not something a prompt prevents — the previous WI's prompt rule reduced it and did
/// not remove it. A system that pays people cannot have "usually it doesn't happen" as its only answer
/// to emitting a thousand identical syllables at a user.
///
/// ★ AND IT REUSES THE FAILURE PATH THAT ALREADY EXISTS. A detected collapse is not a special new
/// state: it is a technical failure, exactly like the provider timing out. The caller yields the error
/// event the user's client already knows how to render — the warning card with Retry — and, because the
/// assistant row is only persisted after a clean finish, nothing degenerate is stored. "Something went
/// wrong, try again" is TRUE, and a retry against a robust model almost always succeeds. A thousand
/// repetitions is worse than an error: the user cannot tell it from an opinion.
/// </summary>
public sealed class DegenerationGuard(
    int maxConsecutiveRepeats = DegenerationGuard.DefaultMaxConsecutiveRepeats,
    int maxCharacters = DegenerationGuard.DefaultMaxCharacters)
{
    /// <summary>
    /// How many times one identical word may follow itself before the answer is declared broken.
    ///
    /// ★ TWELVE IS NOT ARBITRARY. Prose does not repeat a word twelve times consecutively with nothing
    /// between — not in a list, not in a table (the cells' punctuation is not word characters, so a
    /// column of "no" still reads as consecutive repeats, which is the only near-miss worth naming, and
    /// twelve identical cells in a row is already a table nobody wants). Meanwhile the observed collapse
    /// ran to hundreds. Sitting at twelve catches it while it is still a glitch rather than a wall, and
    /// leaves ordinary emphasis ("no, no, no") far below the line.
    /// </summary>
    public const int DefaultMaxConsecutiveRepeats = 12;

    /// <summary>
    /// The ceiling on a single answer, in characters.
    ///
    /// Measured against real answers rather than guessed: the longest legitimate explanation observed
    /// for a three-rule plan was about 5,300 characters. Twenty thousand is roughly four times that, so
    /// a plan with many more rules still fits comfortably, while a runaway generation — which has no
    /// natural end — hits it. This is the backstop for a collapse that does NOT repeat a single word:
    /// a loop over a whole phrase, for instance.
    /// </summary>
    public const int DefaultMaxCharacters = 20_000;

    private readonly StringBuilder _pendingWord = new();
    private string? _previousWord;
    private int _repeats = 1;
    private int _characters;

    /// <summary>True once the answer has been judged degenerate. Never returns to false.</summary>
    public bool IsDegenerate { get; private set; }

    /// <summary>Why, for the log. Null until <see cref="IsDegenerate"/> is true. Carries no user text.</summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// Feeds the next fragment in. Returns true when THIS fragment completes a degenerate answer, at
    /// which point the caller must NOT forward the fragment and must fail the turn.
    ///
    /// ★ CALLED BEFORE THE FRAGMENT IS SENT, deliberately. Streaming means some repetition has already
    /// reached the screen by the time a run is provable — that is inherent, not a bug — but the limit is
    /// what bounds it: at most a dozen repeats appear instead of hundreds, and the offending fragment
    /// itself is never forwarded.
    /// </summary>
    public bool Observe(string? fragment)
    {
        if (IsDegenerate) return true;
        if (string.IsNullOrEmpty(fragment)) return false;

        _characters += fragment.Length;

        if (_characters > maxCharacters)
        {
            return Fail($"the answer passed {maxCharacters} characters without ending");
        }

        foreach (var character in fragment)
        {
            // A "word" is a maximal run of letters and digits. Everything else — spaces, punctuation,
            // markdown pipes and asterisks — is a boundary. Fragments split words arbitrarily, so a
            // partial word is carried over to the next call rather than counted twice.
            if (char.IsLetterOrDigit(character))
            {
                _pendingWord.Append(character);
                continue;
            }

            if (CompleteWord())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Closes the answer, catching a run that ended on the very last word with no trailing punctuation.
    /// </summary>
    public bool Finish() => !IsDegenerate && CompleteWord();

    private bool CompleteWord()
    {
        if (_pendingWord.Length == 0) return false;

        var word = _pendingWord.ToString();
        _pendingWord.Clear();

        if (string.Equals(word, _previousWord, StringComparison.OrdinalIgnoreCase))
        {
            _repeats++;

            if (_repeats >= maxConsecutiveRepeats)
            {
                // ★ THE WORD ITSELF IS NOT IN THE REASON. It is a fragment of the user's own answer
                // about their own plan, and a log is not where that belongs — the same rule the tools
                // follow with references and plan names. The count is what an operator needs.
                return Fail($"a single word repeated {_repeats} times consecutively");
            }

            return false;
        }

        _previousWord = word;
        _repeats = 1;
        return false;
    }

    private bool Fail(string reason)
    {
        IsDegenerate = true;
        Reason = reason;
        return true;
    }
}
