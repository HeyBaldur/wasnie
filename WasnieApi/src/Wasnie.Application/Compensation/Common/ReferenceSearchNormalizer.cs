namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// FOLDS THE LOOK-ALIKE CHARACTERS OUT OF A REFERENCE THE USER TYPED OR PASTED, SO THAT A SEARCH
/// MATCHES WHAT THE SCREEN SHOWED.
///
/// ★★ WHAT HAPPENED (KAN-81). A user pasted <c>HUBSPOT-517982827731</c> and the search came back empty,
/// and the assistant told them three times to "copy the identifier exactly". They had. The characters
/// at fault are invisible: U+2011 (non-breaking hyphen) and U+002D (hyphen-minus) render identically at
/// any normal size, and so do a non-breaking space and a space.
///
/// ★★ ONLY THE INPUT IS NORMALISED — NEVER THE COLUMN. This is the decision worth keeping, and the
/// measurement behind it: of 10,827 rows in <c>CompensationTransactions</c>, ZERO have a single
/// non-ASCII character in <c>ReferenceNumber</c>. The stored side is already clean, so wrapping the
/// column in REPLACE() would buy nothing and cost twice — it makes the predicate non-sargable, so every
/// reference lookup becomes a scan, and it moves a cleaning step onto the READ path, which is what
/// §D1 exists to forbid. If dirty references are ever ingested, the fix belongs in ingestion, beside
/// <see cref="TransactionCreateGuard"/>, not here.
///
/// ★ THE DASHES FOLD TO ONE, THE INVISIBLES DISAPPEAR. Every dash-like code point becomes U+002D
/// because a reference is an identifier, not prose: nothing downstream needs to tell an en dash from a
/// hyphen. Zero-width characters are dropped outright rather than turned into spaces — they carry no
/// width on screen, so a user who pasted one never saw anything there to type.
///
/// ★ WHERE THE ODD CHARACTERS COME FROM, MEASURED. They are not in the data and they are not the
/// user's typing: U+2011 appears in 166 of Zeke's own messages and 3 of the users'. The assistant
/// writes non-breaking hyphens, the user copies its text back, and the copy no longer matches. That is
/// why this normaliser sits on the search path that BOTH the screen and the assistant use — fixing one
/// of the two would leave the other still failing on text the product itself produced.
/// </summary>
public static class ReferenceSearchNormalizer
{
    /// <summary>
    /// Every code point that renders as a horizontal stroke and that a paste can carry in where a
    /// plain hyphen was meant. U+2212 is the mathematical minus, which some spreadsheets emit.
    /// </summary>
    private static readonly char[] DashLike =
    [
        '‐', // hyphen
        '‑', // non-breaking hyphen — the one from the incident
        '‒', // figure dash
        '–', // en dash
        '—', // em dash
        '―', // horizontal bar
        '−', // minus sign
        '﹘', // small em dash
        '﹣', // small hyphen-minus
        '－', // fullwidth hyphen-minus
    ];

    /// <summary>
    /// Characters with no width on screen. A user cannot have seen them, so they cannot have meant
    /// them, so they are removed rather than folded to a space.
    /// </summary>
    private static readonly char[] ZeroWidth =
    [
        '​', // zero-width space
        '‌', // zero-width non-joiner
        '‍', // zero-width joiner
        '⁠', // word joiner
        '﻿', // zero-width no-break space / BOM
    ];

    /// <summary>
    /// Spaces that are not U+0020. They DO have width, so they are kept as a space rather than dropped:
    /// removing them would silently join two words the user sees as separate.
    /// </summary>
    private static readonly char[] SpaceLike =
    [
        ' ', // no-break space
        ' ', // figure space
        ' ', // narrow no-break space
        ' ', // thin space
    ];

    /// <summary>
    /// Returns the term with look-alike characters folded to their plain ASCII equivalent and the ends
    /// trimmed. Null or blank input comes back unchanged, so callers keep their own "no filter" checks.
    /// </summary>
    public static string? Normalize(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return term;

        var builder = new System.Text.StringBuilder(term.Length);

        foreach (var c in term)
        {
            if (Array.IndexOf(ZeroWidth, c) >= 0)
                continue;

            if (Array.IndexOf(DashLike, c) >= 0)
            {
                builder.Append('-');
                continue;
            }

            builder.Append(Array.IndexOf(SpaceLike, c) >= 0 ? ' ' : c);
        }

        return builder.ToString().Trim();
    }
}
