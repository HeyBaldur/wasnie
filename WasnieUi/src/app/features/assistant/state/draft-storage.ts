/**
 * Session-scoped backup for the assistant's per-conversation drafts.
 *
 * ★ sessionStorage, NOT localStorage — and that is a privacy decision, not a technical one. A draft is
 * text the user wrote, and in Incentra it routinely contains payee names and questions about what
 * somebody was paid. `localStorage` keeps that on the disk indefinitely, surviving the browser being
 * closed. `sessionStorage` covers the case that actually happens — an accidental refresh, a navigation
 * — and dies with the tab. It is the smallest thing that solves the problem.
 *
 * ★ MEMORY IS THE SOURCE OF TRUTH; THIS IS A BACKUP. Every method here can fail and the composer must
 * not care: private mode, a full quota and a browser policy all throw from `sessionStorage`, and none
 * of them is a reason a user cannot type or send. Failure degrades to memory, silently and on purpose.
 */

/**
 * The prefix every draft key starts with.
 *
 * ★ Session teardown sweeps by this prefix (see AuthService.clearSessionSilent), so it is a contract
 * between two files rather than a private detail. Changing it means changing the sweep with it.
 */
export const DRAFT_KEY_PREFIX = 'wasnie:draft:assistant';

/**
 * The most text that gets persisted, in characters.
 *
 * sessionStorage has a quota, and one pasted document must not be able to take the OTHER conversations'
 * drafts down with it. Past this the draft still lives in memory and still works — it just is not
 * backed up, which is the right thing to lose first.
 */
const MAX_PERSISTED_CHARS = 20_000;

export type DraftMap = Record<string, string>;

/**
 * Where one user's drafts live.
 *
 * ★ THE KEY CARRIES THE TENANT AND THE USER, and without that this feature is a data leak rather than a
 * convenience: on a shared machine, or after someone switches accounts without closing the tab, the
 * next person would open the assistant and find the previous one's half-written question about a
 * payee's pay. Anonymous (nobody signed in) gets its own bucket rather than sharing one.
 */
export function draftKeyFor(tenantId: string | null, userId: string | null): string {
  return `${DRAFT_KEY_PREFIX}:${tenantId ?? 'anon'}:${userId ?? 'anon'}`;
}

/**
 * Reads the stored map, or an empty one.
 *
 * ★ ANYTHING UNEXPECTED IS DISCARDED, NOT REPAIRED. Storage is text a previous version of this code
 * wrote, or that something else corrupted; trusting its shape would turn a bad string into a crash on
 * the first render of the assistant. An empty map costs the user their drafts once and nothing else.
 */
export function readDrafts(key: string): DraftMap {
  try {
    const raw = sessionStorage.getItem(key);
    if (!raw) {
      return {};
    }

    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      return {};
    }

    // Keep only the entries that are actually drafts. One bad value must not discard the good ones.
    const drafts: DraftMap = {};
    for (const [conversationId, text] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof text === 'string') {
        drafts[conversationId] = text;
      }
    }

    return drafts;
  } catch {
    return {};
  }
}

/** Writes the map, dropping anything too large to be worth a quota error. Never throws. */
export function writeDrafts(key: string, drafts: DraftMap): void {
  try {
    const persistable: DraftMap = {};
    for (const [conversationId, text] of Object.entries(drafts)) {
      if (text.length > 0 && text.length <= MAX_PERSISTED_CHARS) {
        persistable[conversationId] = text;
      }
    }

    if (Object.keys(persistable).length === 0) {
      sessionStorage.removeItem(key);
      return;
    }

    sessionStorage.setItem(key, JSON.stringify(persistable));
  } catch {
    // Memory already holds the truth; a backup that cannot be written changes nothing the user sees.
  }
}

/** Forgets every user's drafts. Called when the session ends. Never throws. */
export function clearAllDrafts(): void {
  try {
    const doomed = Object.keys(sessionStorage).filter(key => key.startsWith(DRAFT_KEY_PREFIX));
    doomed.forEach(key => sessionStorage.removeItem(key));
  } catch {
    // Nothing to do: the tab closing takes them anyway.
  }
}
