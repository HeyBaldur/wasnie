using System.Globalization;
using System.Text;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// The place in the list a batch continues from: the last row's (UpdatedAt, Id).
///
/// ★★ A KEYSET, NOT AN OFFSET, AND THE DIFFERENCE IS CORRECTNESS BEFORE IT IS SPEED. With
/// <c>OFFSET 2000</c> the database walks and discards two thousand rows to reach the ones it wants —
/// but the real defect is that the offset counts a list that MOVES. Every conversation this user
/// touches jumps to the top (the order is UpdatedAt descending), so a thread answered while somebody
/// is paging shifts everything down one place: the row that was at index 25 is now at 26, and the next
/// batch starts past it. The user scrolls and a conversation they have not seen is silently gone,
/// while the one that moved appears twice. Nothing in the UI can detect that. A cursor naming a ROW
/// instead of a POSITION cannot drift, because the row keeps its (UpdatedAt, Id) whatever happens
/// around it.
///
/// ★ TWO COLUMNS, AND THE SECOND IS NOT DECORATION. <c>UpdatedAt</c> alone is not unique — a bulk
/// insert, a seeded fixture, or two turns in the same millisecond all produce ties, and a strict
/// <c>&lt;</c> on the timestamp SKIPS every row sharing the boundary value while a non-strict
/// <c>&lt;=</c> REPEATS them forever. The id breaks the tie and makes the order total, so exactly one
/// row can be "the last one I saw".
///
/// ★ OPAQUE TO THE CLIENT, ON PURPOSE. It is encoded rather than a pair of readable fields so its
/// composition can change — a pin flag, a different sort — without a client that parsed it breaking.
/// The front end never builds one; it echoes back what it was handed. Encoding is NOT security: this
/// says nothing a caller does not already have, and every request is re-scoped to the caller's own
/// conversations regardless of what the cursor claims.
/// </summary>
/// <param name="UpdatedAt">The last row's timestamp, round-tripped to the tick.</param>
/// <param name="Id">The last row's id — the tiebreaker that makes the order total.</param>
public readonly record struct ConversationCursor(DateTimeOffset UpdatedAt, Guid Id)
{
    /// <summary>
    /// The separator. A character that cannot occur in either half, so splitting is unambiguous
    /// without escaping: the timestamp is round-trip ISO and the id is a hyphenated GUID.
    /// </summary>
    private const char Separator = '|';

    /// <summary>
    /// "o" — the round-trip format. ★ NOT the default: the default drops sub-second precision, which
    /// would make the cursor land on a DIFFERENT row than the one it was built from and reintroduce the
    /// duplicate-or-skip this whole type exists to prevent.
    /// </summary>
    private const string TimestampFormat = "o";

    public string Encode()
    {
        var raw = $"{UpdatedAt.ToString(TimestampFormat, CultureInfo.InvariantCulture)}{Separator}{Id:D}";

        // ★ URL-SAFE BY HAND. `Base64Url` is .NET 9 and this targets net8.0. The cursor travels as a
        // query-string parameter, where '+' means a space and '/' and '=' need escaping — a plain
        // Base64 cursor survives most round trips and corrupts on the ones where something helpfully
        // decodes it, which is the worst kind of bug to find later.
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Reads a cursor a client sent back, or null when it is anything other than one this class wrote.
    ///
    /// ★ A BAD CURSOR IS NOT AN ERROR, IT IS THE FIRST PAGE. Cursors reach the client and come back
    /// through URLs, bookmarks and a paste into the wrong tab; a 400 for a stale one turns a harmless
    /// staleness into a broken screen. Starting over is always a correct answer to "continue from a
    /// place that no longer parses", and it cannot leak anything, because the scoping does not depend
    /// on the cursor.
    /// </summary>
    public static ConversationCursor? Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        string raw;
        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            // Base64 decodes in blocks of four; Encode trimmed the padding off, so it goes back on.
            raw = Encoding.UTF8.GetString(
                Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '=')));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            // Valid Base64 of bytes that are not UTF-8 — someone else's opaque token, pasted in.
            return null;
        }

        var split = raw.IndexOf(Separator);
        if (split <= 0 || split == raw.Length - 1)
        {
            return null;
        }

        var parsedTimestamp = DateTimeOffset.TryParseExact(
            raw[..split], TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var updatedAt);

        var parsedId = Guid.TryParseExact(raw[(split + 1)..], "D", out var id);

        return parsedTimestamp && parsedId ? new ConversationCursor(updatedAt, id) : null;
    }
}
