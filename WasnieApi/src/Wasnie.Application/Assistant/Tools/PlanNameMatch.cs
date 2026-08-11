using System.Text;

namespace Wasnie.Application.Assistant.Tools;

/// <summary>
/// MATCHING A PLAN NAME THAT CAME OUT OF A LANGUAGE MODEL.
///
/// ★ THE BUG THIS EXISTS TO END. The assistant explained "Q3 2026 - Plan Comercial" in one message and,
/// two messages later, told the user that plan did not exist. Nothing had changed: the model, rewriting
/// the name into the JSON of its second tool call, typed an EM-DASH where the stored name has a hyphen.
/// A strict <c>==</c> said no, the tool returned its refusal, and the model obediently relayed it — a
/// confident denial of a record it had just described, which is the exact failure mode the whole
/// assistant is built to avoid.
///
/// ★ THE MISTAKE WAS THE STRICTNESS, NOT THE MODEL. Machine-to-machine exactness is right when both
/// ends are machines. One end here is a language model rewriting a human's words, and it will
/// reasonably substitute typographic dashes, pad whitespace and re-case a title — that is what writing
/// prose looks like. The identifier the model handles is a NAME, not a key, and a name has to be
/// compared the way a person compares names.
///
/// ★ AND IT IS STILL AN EXACT MATCH. Normalisation happens on BOTH sides and then the whole string must
/// be equal: this is not a substring, not a fuzzy distance, not a "closest plan". "Q3_Enterprise" and
/// "Q3_SMB" stay different, and "Q3" alone still finds nothing. Widening the comparison to a guess is
/// how the assistant would start explaining the wrong plan's rates — a different and worse bug than the
/// one being fixed.
/// </summary>
public static class PlanNameMatch
{
    /// <summary>
    /// The dash characters a model substitutes for a plain hyphen, all folded to <c>-</c>.
    ///
    /// The reported failure was the em-dash; the en-dash was the other one named. The rest of the family
    /// is here because they are the SAME substitution — a non-breaking hyphen or a minus sign typed into
    /// a title fails identically, and shipping a fix that covers two of eight would leave the same
    /// support ticket waiting on the next one. Each is a 1:1 character mapping, so nothing about the
    /// exactness of the comparison changes.
    /// </summary>
    private static readonly char[] Dashes =
    [
        '‐', // hyphen
        '‑', // non-breaking hyphen
        '‒', // figure dash
        '–', // en dash
        '—', // em dash
        '―', // horizontal bar
        '−', // minus sign
    ];

    /// <summary>
    /// The comparable form of a name: dashes folded, whitespace collapsed to single spaces, trimmed.
    ///
    /// Whitespace is tested with <see cref="char.IsWhiteSpace"/> rather than compared to <c>' '</c>,
    /// because the exotic spaces are as real as the exotic dashes — this same model has already been
    /// observed emitting U+202F (narrow no-break space) in its prose. Case is NOT folded here: it is
    /// handled at comparison time by <c>OrdinalIgnoreCase</c>, so this stays a form a human could read
    /// in a log.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;

        foreach (var character in name)
        {
            if (char.IsWhiteSpace(character))
            {
                // Deferred rather than appended: a run of spaces becomes one, and a run at either end
                // becomes none, without a second Trim pass.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(Array.IndexOf(Dashes, character) >= 0 ? '-' : character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether two names are the same name. Both sides are normalised — normalising only the request
    /// would fix a model that types an em-dash and break a tenant whose plan is STORED with one.
    /// </summary>
    public static bool AreSame(string? stored, string? requested)
    {
        var left = Normalize(stored);
        var right = Normalize(requested);

        return left.Length > 0
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether one name is the other with WHOLE WORDS missing from an end — the shape a model produces
    /// when it drops part of a title it is retyping.
    ///
    /// ★ THE SECOND FAILURE, MEASURED. Asked about "Q3 2026 — Plan Comercial EMEA (Test Integral)", this
    /// model sent the full name five times out of eight and "Plan Comercial EMEA (Test Integral)" — the
    /// quarter prefix silently dropped — the other three. Exact matching correctly refused those three,
    /// and the user was told a plan they were looking at did not exist. Stochastically. That is the
    /// worst possible shape for a bug report.
    ///
    /// ★ WHY WHOLE WORDS, AND NOT A SUBSTRING. A raw <c>Contains</c> would let "Q3" match
    /// "Q3_Enterprise", and answering about a plan the user did not ask for is worse than refusing.
    /// Both sides are padded with spaces before the search, so only complete words at word boundaries
    /// can align: "Q3" does not match "Q3_Enterprise" (one token), and "Plan Comercial EMEA (Test
    /// Integral)" does match "Q3 2026 - Plan Comercial EMEA (Test Integral)".
    ///
    /// ★ IT IS SYMMETRIC, because the model drops words as readily as the user does. Neither side is
    /// the authority on how much of the name gets typed.
    ///
    /// ★ AND ON ITS OWN IT DECIDES NOTHING. This only produces CANDIDATES. The caller resolves a plan
    /// from it only when there is exactly one, and says so in the payload when it does — see the tool.
    /// </summary>
    public static bool IsPartialNameOf(string? stored, string? requested)
    {
        var left = Normalize(stored).ToLowerInvariant();
        var right = Normalize(requested).ToLowerInvariant();

        if (left.Length == 0 || right.Length == 0) return false;
        if (left == right) return true;

        // The padding is what makes this a word match rather than a substring match: " emea " cannot be
        // found inside " emea_overlay ", but it is found inside " emea overlay ".
        var paddedLeft = $" {left} ";
        var paddedRight = $" {right} ";

        return paddedLeft.Contains(paddedRight, StringComparison.Ordinal)
            || paddedRight.Contains(paddedLeft, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fragment of the name that is safe to hand to the DATABASE as a narrowing filter.
    ///
    /// ★ WHY THIS IS NEEDED AT ALL, and it is the half of the fix that is easy to miss. Normalising the
    /// comparison is useless if the row never reaches the comparison. The plan list is narrowed in SQL
    /// by <c>Name.ToLower().Contains(search)</c>, so passing the model's raw " q3 2026 — plan comercial "
    /// returns NOTHING for a plan stored with a hyphen — the candidate list is empty before any
    /// in-memory matching happens, and the refusal comes back exactly as before.
    ///
    /// So the narrowing key is the longest run of characters containing NO dash and NO whitespace: the
    /// part of the name that normalisation cannot alter on either side. "Q3 2026 - Plan Comercial"
    /// yields "comercial", which matches the stored row whatever the separators look like.
    ///
    /// ★ THIS DOES NOT MAKE THE LOOKUP A SUBSTRING SEARCH. The fragment only decides which rows are
    /// FETCHED; <see cref="AreSame"/> still has to accept the whole name afterwards. That separation
    /// already existed — the SQL filter was always a narrowing step and never the match — and it is why
    /// widening it here is safe.
    ///
    /// ★ NO INDEX IS HARMED. <c>Contains</c> compiles to <c>LIKE '%…%'</c>, which is non-sargable: the
    /// query was already a scan over the tenant's plans and a shorter fragment does not change that. It
    /// fetches slightly more rows, all of which the exact match then filters.
    ///
    /// Null when the name is nothing but separators — the caller then lists without narrowing rather
    /// than filtering on an empty string.
    /// </summary>
    public static string? NarrowingKey(string? name)
    {
        var normalized = Normalize(name);
        if (normalized.Length == 0) return null;

        var longest = normalized
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .OrderByDescending(part => part.Length)
            .FirstOrDefault();

        return string.IsNullOrEmpty(longest) ? null : longest;
    }
}
