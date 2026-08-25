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
export function filterConversations(
  items: readonly AssistantConversationSummary[],
  query: string,
): AssistantConversationSummary[] {
  const needle = fold(query);
  if (needle.length === 0) {
    return [...items];
  }

  return items.filter(item => fold(item.title ?? '').includes(needle));
}

function fold(value: string): string {
  return value
    .normalize('NFD')
    // Strip the marks NFD just split off. The Unicode property escape says what it means and
    // keeps this line pure ASCII - a class of literal combining characters is invisible in an
    // editor and does not survive every tool that touches the file.
    .replace(/\p{Diacritic}/gu, '')
    // Polish ł carries no combining mark, so NFD leaves it alone — it has to be mapped by hand.
    .replace(/ł/g, 'l')
    .replace(/Ł/g, 'L')
    .toLowerCase()
    .trim();
}
