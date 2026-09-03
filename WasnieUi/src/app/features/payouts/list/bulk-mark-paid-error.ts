/**
 * Turns one anti-double-pay refusal from the bulk mark-paid endpoint into something a person can
 * read.
 *
 * ★★ WHAT THE SERVER SENDS TODAY IS ONE UNBROKEN LINE. `BulkMarkPaidHandler` builds it by string
 * concatenation:
 *
 *   Payout <guid> (<Payee Name>): 15 transaction(s) already paid — credit <guid> (tx <guid>)
 *   consumed by <guid>, credit <guid> (tx <guid>) consumed by <guid>, credit …
 *
 * Five of those in a row is roughly nine hundred characters of comma-separated GUIDs in a single
 * paragraph, and the reader cannot tell where one conflict ends and the next begins. Nothing about
 * the CONTENT is wrong — every identifier there is the one somebody needs in order to find the row
 * — it is the shape that makes it unusable.
 *
 * ★ THIS IS A PRESENTATION SHIM, AND IT KNOWS IT. The right fix is the one §C1 describes: the
 * handler emits a CODE with structured parameters and the front end phrases it, the way
 * `PayoutSkipReason` and the rate-table invariants already do. That is a backend change with its
 * own contract, its own tests and its own work item. Until it happens, the string is parsed HERE —
 * and parsed DEFENSIVELY: every field is optional, and a line that does not match the expected
 * shape comes back with `payeeName: null`, which the template renders exactly as it does today.
 * A reworded message degrades to the current display; it never blanks the error out.
 *
 * ★ THE IDENTIFIERS ARE NOT SHORTENED. They look like noise, and truncating them to eight
 * characters would look much tidier — but the only reason they are on screen at all is so somebody
 * can paste one into a query. Structure and monospace make them scannable; abbreviation would make
 * them decorative.
 */

/** One credit that was already consumed by another payout. */
export interface BulkMarkPaidConflict {
  creditId: string;
  transactionId: string;
  consumedByPayoutId: string;
}

/** One refusal, taken apart. `raw` is always the original line. */
export interface BulkMarkPaidErrorBlock {
  /** Null when the line did not match the expected shape — the signal to render `raw` verbatim. */
  payeeName: string | null;
  payoutId: string | null;
  /** The unparsed remainder of the headline, used only when `totalConflicts` could not be read. */
  summary: string | null;
  /** How many credits the server says are in conflict, which is NOT how many it listed. */
  totalConflicts: number | null;
  conflicts: BulkMarkPaidConflict[];
  /**
   * Conflicts the server counted but did not name.
   *
   * ★ THE SERVER SENDS ONLY THE FIRST THREE (`.Take(3)`), while the count in the headline is the
   * full total. Today a reader sees "15 transaction(s) already paid" above three lines and has no
   * way to know whether the other twelve were forgotten or withheld. Saying so is the single
   * biggest thing this rewrite adds.
   */
  omittedConflicts: number;
  /**
   * The distinct payouts that consumed those credits — almost always exactly ONE.
   *
   * ★ WHEN THERE IS ONE, IT IS SAID ONCE. Every conflict in a refusal names the payout that took
   * the credit, and in practice it is the same payout every time: three identical 36-character
   * GUIDs stacked under each other, which is three lines of nothing. Hoisted to the header it
   * becomes the sentence the reader actually wants — "these are already paid by THAT payout" — and
   * each conflict shrinks to the two ids that differ. When the ids genuinely differ the header
   * stays quiet and each conflict carries its own.
   */
  consumedByPayoutIds: string[];
  /** The original line, always. What the template falls back to when `payeeName` is null. */
  raw: string;
}

/** `Payout <id> (<Name>): <rest>` — the name may contain anything, so it is matched lazily. */
const HEADLINE = /^Payout\s+(\S+)\s+\((.+?)\):\s*(.+)$/;

/** `credit <id> (tx <id>) consumed by <id>` */
const CONFLICT = /credit\s+(\S+)\s+\(tx\s+(\S+)\)\s+consumed by\s+(\S+?)(?=,\s*credit\s|$)/g;

/** The leading count of `15 transaction(s) already paid`. */
const LEADING_COUNT = /^(\d+)\b/;

/** An em dash with space either side: the seam between the headline and the conflict list. */
const SEAM = ' — ';

function unparsed(raw: string): BulkMarkPaidErrorBlock {
  return {
    payeeName: null,
    payoutId: null,
    summary: null,
    totalConflicts: null,
    conflicts: [],
    omittedConflicts: 0,
    consumedByPayoutIds: [],
    raw,
  };
}

export function parseBulkMarkPaidError(raw: string): BulkMarkPaidErrorBlock {
  const seamAt = raw.indexOf(SEAM);
  const headline = seamAt === -1 ? raw : raw.slice(0, seamAt);
  const detail = seamAt === -1 ? '' : raw.slice(seamAt + SEAM.length);

  const parts = HEADLINE.exec(headline);
  if (!parts) {
    return unparsed(raw);
  }

  const [, payoutId, payeeName, summary] = parts;

  const conflicts: BulkMarkPaidConflict[] = [];
  // exec with /g keeps state on the regex object, so it is reset before every use.
  CONFLICT.lastIndex = 0;
  let match: RegExpExecArray | null;
  while ((match = CONFLICT.exec(detail)) !== null) {
    conflicts.push({ creditId: match[1], transactionId: match[2], consumedByPayoutId: match[3] });
  }

  const counted = LEADING_COUNT.exec(summary);
  const totalConflicts = counted ? Number(counted[1]) : null;

  return {
    payeeName,
    payoutId,
    summary,
    totalConflicts,
    conflicts,
    // Never negative: if the server ever lists more than it counts, "0 more" is the honest answer.
    omittedConflicts: totalConflicts === null ? 0 : Math.max(0, totalConflicts - conflicts.length),
    consumedByPayoutIds: [...new Set(conflicts.map((c) => c.consumedByPayoutId))],
    raw,
  };
}
