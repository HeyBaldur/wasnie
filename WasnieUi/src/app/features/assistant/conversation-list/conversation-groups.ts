import { AssistantConversationSummary } from '../models/assistant.model';

/** The time bands a conversation list is cut into, newest first. */
export type ConversationGroupKey = 'today' | 'yesterday' | 'last7' | 'older';

export interface ConversationGroup {
  key: ConversationGroupKey;
  /** Translation key for the band's heading. */
  labelKey: string;
  items: AssistantConversationSummary[];
}

const LABEL_KEYS: Record<ConversationGroupKey, string> = {
  today: 'ASSISTANT.GROUP_TODAY',
  yesterday: 'ASSISTANT.GROUP_YESTERDAY',
  last7: 'ASSISTANT.GROUP_LAST_7',
  older: 'ASSISTANT.GROUP_OLDER',
};

const ORDER: ConversationGroupKey[] = ['today', 'yesterday', 'last7', 'older'];

/** Midnight of the day `date` falls on, in the reader's own timezone. */
function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

/**
 * Which band a conversation belongs to, relative to `now`.
 *
 * ★ CALENDAR DAYS, NOT ELAPSED HOURS. "Yesterday" has to mean the day before this one — a conversation
 * from 23:50 last night is yesterday's at 00:10 today, even though barely twenty minutes have passed.
 * Subtracting 24h instead would file it under "today" and the heading would be a lie the user can check
 * against their own memory.
 *
 * ★ AND THE DAYS ARE THE READER'S. `new Date(iso)` parses the stored UTC instant and the getters read it
 * back in local time, so someone in Warsaw and someone in Santiago see their own midnight, not the
 * server's.
 */
export function groupKeyFor(updatedAt: string, now: Date): ConversationGroupKey {
  const when = new Date(updatedAt);
  // An unparseable timestamp must not throw the whole list away; it sinks to the bottom band.
  if (Number.isNaN(when.getTime())) {
    return 'older';
  }

  const days = Math.round(
    (startOfDay(now).getTime() - startOfDay(when).getTime()) / 86_400_000);

  // A future timestamp (clock skew between the server and this machine) is not "older" — the row is
  // the freshest thing in the list, so it belongs at the top with today's.
  if (days <= 0) {
    return 'today';
  }
  if (days === 1) {
    return 'yesterday';
  }
  if (days <= 7) {
    return 'last7';
  }
  return 'older';
}

/**
 * Cuts a conversation list into time bands, keeping the order it arrived in within each band.
 *
 * Empty bands are dropped rather than rendered as a heading with nothing under it.
 */
export function groupConversations(
  items: readonly AssistantConversationSummary[],
  now: Date,
): ConversationGroup[] {
  const buckets = new Map<ConversationGroupKey, AssistantConversationSummary[]>();

  for (const item of items) {
    const key = groupKeyFor(item.updatedAt, now);
    const bucket = buckets.get(key);
    if (bucket) {
      bucket.push(item);
    } else {
      buckets.set(key, [item]);
    }
  }

  return ORDER
    .filter(key => buckets.has(key))
    .map(key => ({ key, labelKey: LABEL_KEYS[key], items: buckets.get(key)! }));
}

/**
 * Filters by title, case- and accent-insensitively.
 *
 * ★ ACCENT-FOLDED, because the titles are Spanish and Polish as often as English: someone typing
 * "comision" must find "Comisión", and someone typing "zwrot" must find "Zwrót". A plain
 * `toLowerCase().includes()` finds neither, and the user has no way to tell that the thread is there.
 */

// ★ filterConversations AND ITS fold() LIVED HERE AND ARE GONE ON PURPOSE.
//
// They matched a title case- and accent-insensitively across the LOADED list, which was the right
// answer while the whole list was loaded. The list is paged now, and a filter over the loaded batch
// answers "no results" while the match sits further down, unfetched — the same class of untruth as
// telling somebody a record does not exist because the lookup could not reach it. The search is a
// parameter of the list endpoint now, and the insensitivity is the Title column's collation.
//
// Deleted rather than left unused: a helper that still exists is a helper somebody wires back in.
