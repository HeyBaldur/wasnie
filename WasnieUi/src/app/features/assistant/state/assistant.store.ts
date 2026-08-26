import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AssistantApiService } from '../services/assistant.api.service';
import { AuthService } from '../../../core/services/auth.service';
import { DraftMap, draftKeyFor, readDrafts, writeDrafts } from './draft-storage';
import {
  AssistantConversation,
  AssistantConversationSummary,
  AssistantMessage,
  AssistantProgressStep,
  isCancelledReply,
} from '../models/assistant.model';

/**
 * Everything one conversation's turn-in-flight needs, held per conversation.
 *
 * ★ THE WHOLE TURN LIVES IN ONE OBJECT so it can only ever be moved, cleared or abandoned as a unit.
 * Split across separate maps, "which conversation is streaming" and "whose partial text is this" could
 * disagree — which is a smaller version of the bug that made this type necessary.
 */
export interface AssistantStreamState {
  /** The answer as far as it has arrived; null when nothing is being written for this conversation. */
  reply: string | null;
  /** The steps the server announced for this turn, in order. */
  steps: AssistantProgressStep[];
  /** True while a request for this conversation is out. */
  sending: boolean;
  /** A translation key for this conversation's last failure. */
  errorKey: string | null;
  /** The words of a turn that never reached the server, so they can be handed back. */
  unsent: string | null;
  /** The handle Stop pulls — for THIS conversation, never for whichever one is open. */
  controller: AbortController | null;
  /** True from the click on Stop until this conversation's aborted request has finished unwinding. */
  cancelling: boolean;
}

/**
 * The assistant panel's state: open/closed, the current thread, and the history list.
 *
 * Root-provided because the panel lives in the app shell and its trigger lives in the topbar — two
 * components that never meet in the tree. The store is what connects them.
 */
@Injectable({ providedIn: 'root' })
export class AssistantStore {
  private readonly api = inject(AssistantApiService);
  private readonly auth = inject(AuthService);

  // ── Entitlement ───────────────────────────────────────────────────────────
  // Null until asked. The trigger renders nothing while unknown, so a slow answer never flashes a
  // button the user is not entitled to.
  readonly entitled = signal<boolean | null>(null);

  /**
   * True when the assistant is withheld ONLY because the workspace is on Free. Drives the locked
   * entry point; stays false for every other refusal, which keeps rendering nothing at all.
   */
  readonly requiresUpgrade = signal(false);

  // ── Panel ─────────────────────────────────────────────────────────────────
  readonly isOpen = signal(false);
  readonly historyOpen = signal(false);

  // ── Data ──────────────────────────────────────────────────────────────────
  readonly conversation = signal<AssistantConversation | null>(null);
  readonly conversations = signal<AssistantConversationSummary[]>([]);
  /**
   * Something in the CHAT PANE is loading: opening a conversation, or starting one.
   *
   * ★★ NOT THE HISTORY LIST — AND CONFLATING THEM WAS A REAL, VISIBLE BUG. When the list's loader was
   * driven off this signal, clicking a row in the rail made the RAIL blank to "Loading…" and come
   * back: a blink in the sidebar every single time somebody changed conversation, caused by an
   * operation that has nothing to do with the sidebar. Two things that can be true independently need
   * two signals; the streaming map next door exists for the same reason.
   */
  readonly loading = signal(false);

  /** A failure in the CHAT PANE. See `listError` for the rail's own. */
  readonly error = signal<string | null>(null);

  /** True only while the FIRST batch of the history list is in flight. */
  readonly listLoading = signal(false);

  /**
   * The caller's pinned conversations, newest pin first.
   *
   * ★★ A SEPARATE LIST, NOT A FLAG ON THE ROWS IN `conversations`. The server EXCLUDES pinned
   * threads from the paged flow, so they are not in that array at all — by design, because a pinned
   * conversation that also appeared in its time band would render twice. Keeping them apart here is
   * what makes that true on screen instead of only in the response.
   */
  readonly pinnedConversations = signal<AssistantConversationSummary[]>([]);

  /** True when this conversation is in the pinned group. Drives the menu label and the row marker. */
  isPinned(conversationId: string): boolean {
    return this.pinnedConversations().some((c) => c.id === conversationId);
  }

  /**
   * A failure loading the history list.
   *
   * ★ SEPARATE FROM `error` FOR THE SAME REASON THE LOADER IS. Sharing it meant a conversation that
   * failed to OPEN put "your conversations could not be loaded" in the sidebar — a sentence about the
   * rail, raised by something the rail did not do.
   */
  readonly listError = signal<string | null>(null);

  readonly messages = computed<AssistantMessage[]>(() => this.conversation()?.messages ?? []);
  readonly hasConversation = computed(() => this.conversation() !== null);

  // ── Per-conversation drafts ───────────────────────────────────────────────

  /**
   * The key a draft is filed under when the conversation does not exist on the server yet.
   *
   * ★ A NEW CONVERSATION IS A CONVERSATION. Someone who types into an empty assistant, goes to read an
   * old thread and comes back expects their words to be there — "I had not sent it yet" is the whole
   * point of a draft. Without a key of its own that text had nowhere to live and was simply lost.
   */
  static readonly NEW_CONVERSATION_DRAFT = '__new__';

  /**
   * Every unsent draft, by conversation.
   *
   * ★ A MAP, NOT ONE STRING — that single string IS the bug. It belonged to whichever conversation was
   * open last, so typing in A and switching to B showed A's half-written question sitting in B's
   * composer, ready to be sent to the wrong thread.
   *
   * ★ AND MEMORY IS THE SOURCE OF TRUTH. sessionStorage is a backup that can fail; see draft-storage.
   */
  /**
   * ★ SEEDED FROM STORAGE AT FIELD INITIALISATION, which happens exactly once: this store is
   * `providedIn: 'root'`, so that is one restore per page load — precisely the event the backup exists
   * for. Restoring from a component instead would re-read on every mount of the drawer and of the page,
   * and the two would race to overwrite each other's map.
   */
  private readonly drafts = signal<DraftMap>(readDrafts(this.draftStorageKey()));

  /** Which storage bucket this user's drafts belong in — tenant and user, see draftKeyFor. */
  private draftStorageKey(): string {
    return draftKeyFor(this.auth.tenantId(), this.auth.currentUser()?.userId ?? null);
  }

  /** The key the composer is reading and writing right now. */
  readonly activeDraftKey = computed(
    () => this.conversation()?.id ?? AssistantStore.NEW_CONVERSATION_DRAFT);

  /** The draft for the conversation on screen, or empty. */
  readonly activeDraft = computed(() => this.drafts()[this.activeDraftKey()] ?? '');

  /** Restores the backup. Safe to call more than once; a failed read yields an empty map. */
  loadDrafts(): void {
    this.drafts.set(readDrafts(this.draftStorageKey()));
  }

  setDraft(conversationKey: string, text: string): void {
    this.drafts.update(all => ({ ...all, [conversationKey]: text }));
    this.persistDrafts();
  }

  clearDraft(conversationKey: string): void {
    this.drafts.update(all => {
      const { [conversationKey]: _removed, ...rest } = all;
      return rest;
    });
    this.persistDrafts();
  }

  /**
   * Moves a draft onto the id the server just assigned.
   *
   * ★ OTHERWISE IT IS ORPHANED. Text typed before the first send is filed under the new-conversation
   * key; the moment the thread gets a real id, the composer starts reading that id instead — and the
   * draft would still be sitting under a key nothing looks at any more, invisible and undeletable.
   */
  private migrateNewConversationDraft(conversationId: string): void {
    const pending = this.drafts()[AssistantStore.NEW_CONVERSATION_DRAFT];
    if (pending === undefined) {
      return;
    }

    this.drafts.update(all => {
      const { [AssistantStore.NEW_CONVERSATION_DRAFT]: _moved, ...rest } = all;
      return { ...rest, [conversationId]: pending };
    });
    this.persistDrafts();
  }

  private persistDrafts(): void {
    writeDrafts(this.draftStorageKey(), this.drafts());
  }

  // ── Per-conversation streaming ────────────────────────────────────────────

  /**
   * Everything about the turn in flight, by conversation.
   *
   * ★ A MAP, NOT A HANDFUL OF GLOBAL SIGNALS — and those globals WERE the bug, twice over. Asking in A
   * and switching to B showed A's loader, A's steps and A's Stop button inside B, even when B was a
   * brand new empty thread. Worse than the wrong indicator: every write went to "the conversation that
   * is open", so the answer to A's question was appended to whatever thread the user happened to be
   * reading when it landed. In a product where an answer can name a payee's balance, dropping it into
   * an unrelated thread is contamination, not a cosmetic glitch.
   *
   * ★ SAME KEY SPACE AS THE DRAFTS, deliberately: `activeDraftKey()` — a real conversation id, or the
   * new-conversation slot — and the same migration when the server assigns the real id. It is the same
   * problem (state that belongs to a conversation that does not exist yet) and it gets the same answer,
   * not a second one invented alongside it.
   */
  private readonly streams = signal<Record<string, AssistantStreamState>>({});

  /** What a conversation nobody has asked anything in looks like. Shared: it is never mutated. */
  private static readonly IDLE_STREAM: AssistantStreamState = {
    reply: null,
    steps: [],
    sending: false,
    errorKey: null,
    unsent: null,
    controller: null,
    cancelling: false,
  };

  /**
   * The streaming state of the conversation ON SCREEN — the only one the UI is ever allowed to render.
   *
   * ★ NO ENTRY MEANS NO INDICATOR. A conversation that has never been asked anything simply is not in
   * the map, and reading falls through to the idle shape: no loader, no steps, no Stop button. That is
   * what makes a freshly opened thread clean while another one is still being answered.
   */
  private readonly activeStream = computed<AssistantStreamState>(
    () => this.streams()[this.activeDraftKey()] ?? AssistantStore.IDLE_STREAM);

  /** True while THIS conversation is waiting on an answer. */
  readonly sending = computed(() => this.activeStream().sending);

  /**
   * The answer as it arrives, before it is a stored row. Null when nothing is streaming for the open
   * conversation — including after a failure, because the server persisted nothing and half an answer
   * on screen would be a message the user cannot find again.
   */
  readonly streamingReply = computed(() => this.activeStream().reply);

  /** A translation key for the open conversation's last failure, rendered in the reader's language. */
  readonly errorKey = computed(() => this.activeStream().errorKey);

  /**
   * The steps of the open conversation's turn in flight, in the order the server announced them.
   *
   * ★ APPEND-ONLY, AND ONLY FROM THE SERVER. Nothing here is predicted: a step exists because the
   * backend said it started, and turns green because the backend said it finished. A stream that sends
   * no progress frames at all — an older backend, or a turn that fails before any work — simply leaves
   * this empty, and the panel falls back to the plain loader it has always shown.
   */
  readonly progressSteps = computed(() => this.activeStream().steps);

  /** True when nothing about this conversation is worth remembering any more. */
  private static isIdle(state: AssistantStreamState): boolean {
    return state.reply === null
      && state.steps.length === 0
      && !state.sending
      && state.errorKey === null
      && state.unsent === null
      && state.controller === null
      && !state.cancelling;
  }

  /**
   * Folds a change into ONE conversation's streaming state.
   *
   * ★ AN ENTRY THAT GOES BACK TO IDLE IS DELETED, not left behind as a row of nulls. The map is read as
   * "which conversations are busy", and a finished turn that keeps its slot would answer that question
   * wrongly for every future reader of it.
   */
  private patchStream(key: string, patch: Partial<AssistantStreamState>): void {
    this.streams.update((all) => {
      const next = { ...(all[key] ?? AssistantStore.IDLE_STREAM), ...patch };

      if (AssistantStore.isIdle(next)) {
        const { [key]: _finished, ...rest } = all;
        return rest;
      }

      return { ...all, [key]: next };
    });
  }

  /**
   * Moves a live stream onto the id the server just assigned.
   *
   * ★ WITHOUT THIS THE ANSWER IS ORPHANED. The first question in a fresh panel starts under the
   * new-conversation slot; the instant the thread gets a real id, everything that reads streaming state
   * — the composer, the loader, `cancel()` — starts looking under that id instead, and the turn still
   * being written would be filed where nothing looks. Exactly the draft's problem, exactly its fix.
   */
  private migrateStream(from: string, to: string): void {
    const moving = this.streams()[from];
    if (moving === undefined) {
      return;
    }

    this.streams.update((all) => {
      const { [from]: _moved, ...rest } = all;
      return { ...rest, [to]: moving };
    });
  }

  /**
   * Ends a conversation's turn and forgets it — used when the conversation itself is going away.
   *
   * The abort is what stops the server too: it sees the connection drop and stops paying for tokens
   * nobody will ever read. See `cancel()` for the same mechanism used deliberately.
   */
  private abandonStream(key: string): void {
    const entry = this.streams()[key];
    if (entry === undefined) {
      return;
    }

    entry.controller?.abort();
    this.streams.update((all) => {
      const { [key]: _gone, ...rest } = all;
      return rest;
    });
  }

  /**
   * Puts one conversation into a given streaming state directly, with no request behind it.
   *
   * ★ THE SEAM THE SPECS USE, and it has to name a conversation. Rendering a mid-stream panel used to
   * be `streamingReply.set(...)`; with the state per conversation there is no global to poke, and a
   * test that does not say WHICH conversation it means is asking the question this WI exists to answer.
   * Production code never calls it — `exchange` owns the map.
   */
  setStreamState(key: string, patch: Partial<AssistantStreamState>): void {
    this.patchStream(key, patch);
  }

  async loadEntitlement(): Promise<void> {
    try {
      const result = await firstValueFrom(this.api.getEntitlement());
      this.entitled.set(result.enabled);
      this.requiresUpgrade.set(result.requiresUpgrade === true);
    } catch {
      // A failed check means "no button", never "assume yes". The backend gates every call anyway,
      // so guessing generously here would only produce a button that 403s. The upsell is suppressed
      // too: we do not know the plan, and inventing an upgrade prompt is worse than showing nothing.
      this.entitled.set(false);
      this.requiresUpgrade.set(false);
    }
  }

  /** Opens the panel. Loads the history once so the user can reach previous threads immediately. */
  async open(): Promise<void> {
    this.isOpen.set(true);
    if (this.conversations().length === 0) {
      await this.loadConversations();
    }
  }

  close(): void {
    this.isOpen.set(false);
    this.historyOpen.set(false);
  }

  toggleHistory(): void {
    this.historyOpen.update((v) => !v);
  }

  // ── The history list: one batch at a time, searched on the server ──────────

  /**
   * Where the next batch starts, or null when the list is complete.
   *
   * ★ IT COMES FROM THE SERVER AND IS NEVER BUILT HERE. The client echoes back what it was handed, so
   * the server can change what a cursor is made of without this file knowing.
   */
  readonly conversationsCursor = signal<string | null>(null);

  /** True while a "Load more" is in flight — distinct from `loading`, which is the first batch. */
  readonly loadingMore = signal(false);

  /** True when there is another batch to ask for. Drives the button's existence, not its enabled state. */
  readonly hasMoreConversations = computed(() => this.conversationsCursor() !== null);

  /**
   * What the search box holds, after the debounce. Empty means "not searching".
   *
   * ★ THE APPLIED TERM, NOT THE TYPED ONE. The box's own text lives in the box; this is what the last
   * request actually asked for, which is what the empty state has to name ("no results for X") — naming
   * the half-typed text would put a word on screen that was never searched for.
   */
  readonly searchTerm = signal('');

  /** Shortest term worth a round trip. Mirrors AssistantPaging.MinSearchLength on the server. */
  static readonly MIN_SEARCH_LENGTH = 2;

  /** How long the typing has to stop before a search is worth a request. */
  private static readonly SEARCH_DEBOUNCE_MS = 300;

  private searchDebounce: ReturnType<typeof setTimeout> | null = null;

  /**
   * ★★ THE TOKEN THAT DISCARDS A STALE ANSWER. Every request takes the next number; when one comes back
   * it is applied only if it is still the newest. Without this, typing "asignacion" fires a request for
   * "asig" and one for "asignacion", and if the FIRST is slower its results land last — the user reads
   * matches for a word they finished typing a second ago, with no way to tell. Cancelling the HTTP call
   * would work too; this also covers the case where the response is already in flight and cannot be
   * recalled.
   */
  private listRequestSeq = 0;

  /**
   * Loads the FIRST batch, honouring whatever search is active. Replaces the list rather than appending.
   */
  async loadConversations(): Promise<void> {
    const seq = ++this.listRequestSeq;

    this.listLoading.set(true);
    this.listError.set(null);

    try {
      const page = await firstValueFrom(
        this.api.listConversations(null, this.effectiveSearch()));

      if (seq !== this.listRequestSeq) {
        return;
      }

      this.conversations.set(page.items);
      this.conversationsCursor.set(page.nextCursor);
      // ★ ONLY THE FIRST BATCH CARRIES IT, and a search carries none at all — searching is a
      // different mode and its results come back flat. Setting it from every response keeps this in
      // step with the server rather than guessing when to clear it.
      this.pinnedConversations.set(page.pinned ?? []);
    } catch {
      if (seq === this.listRequestSeq) {
        this.listError.set('LOAD_FAILED');
      }
    } finally {
      if (seq === this.listRequestSeq) {
        this.listLoading.set(false);
      }
    }
  }

  /**
   * Appends the next batch.
   *
   * ★ APPENDS, AND DEDUPES WHILE IT DOES. The cursor cannot return a row twice on its own — that is the
   * property it exists for — but a conversation the user answers between two clicks jumps to the top of
   * the SERVER'S list while a copy of it is already rendered here from an earlier batch. Filtering by id
   * is cheap and makes a duplicate row impossible regardless of what moved.
   */
  async loadMoreConversations(): Promise<void> {
    const cursor = this.conversationsCursor();

    if (cursor === null || this.loadingMore() || this.listLoading()) {
      return;
    }

    const seq = ++this.listRequestSeq;
    this.loadingMore.set(true);
    this.listError.set(null);

    try {
      const page = await firstValueFrom(
        this.api.listConversations(cursor, this.effectiveSearch()));

      if (seq !== this.listRequestSeq) {
        return;
      }

      const known = new Set(this.conversations().map((c) => c.id));
      this.conversations.update((all) => [
        ...all,
        ...page.items.filter((c) => !known.has(c.id)),
      ]);
      this.conversationsCursor.set(page.nextCursor);
    } catch {
      if (seq === this.listRequestSeq) {
        this.listError.set('LOAD_MORE_FAILED');
      }
    } finally {
      if (seq === this.listRequestSeq) {
        this.loadingMore.set(false);
      }
    }
  }

  /**
   * The search box changed.
   *
   * ★ DEBOUNCED HERE RATHER THAN IN THE COMPONENT, so the drawer and the full page cannot end up with
   * two different ideas of how long to wait — and so the pending timer is cancelled by whichever of them
   * is on screen.
   *
   * ★ AND A TERM BELOW THE MINIMUM RESTORES THE ORDINARY LIST rather than searching for it. One
   * character matches most titles, so those "results" would be the list with extra latency and a
   * misleading heading; the user is mid-word, not asking a question.
   */
  setSearch(term: string): void {
    if (this.searchDebounce !== null) {
      clearTimeout(this.searchDebounce);
    }

    this.searchDebounce = setTimeout(() => {
      this.searchDebounce = null;

      const applied = term.trim();
      const next = applied.length >= AssistantStore.MIN_SEARCH_LENGTH ? applied : '';

      // Nothing changed in terms the SERVER cares about — "a" and "ab"→"a" are both "no search" — so
      // there is nothing to ask for. Without this, backspacing through a word fires a request per key.
      if (next === this.searchTerm()) {
        return;
      }

      this.searchTerm.set(next);
      void this.loadConversations();
    }, AssistantStore.SEARCH_DEBOUNCE_MS);
  }

  // ---- Pinning ------------------------------------------------------------

  /**
   * Pins a conversation, moving it out of its time band and into the pinned group immediately.
   *
   * ★★ OPTIMISTIC, AND THE REVERT IS THE HALF THAT MATTERS. Pinning is a click on a menu item and
   * the answer is a round trip away; waiting for it means the row sits still long enough to look
   * broken, and people click again. So the move happens now — and if the server refuses (the cap,
   * a conversation somebody else deleted underneath us) the rows go back EXACTLY where they were and
   * the failure is said out loud. A silent revert is worse than no optimism: the list would flick and
   * settle back with no explanation.
   *
   * ★ THE SNAPSHOT IS OF BOTH LISTS. The row leaves one array and joins the other, so restoring
   * only the one that failed would leave a duplicate or a hole.
   */
  async pinConversation(conversationId: string): Promise<void> {
    const conversation = this.conversations().find((c) => c.id === conversationId)
      ?? this.pinnedConversations().find((c) => c.id === conversationId);

    if (!conversation || this.isPinned(conversationId)) {
      return;
    }

    const previousPinned = this.pinnedConversations();
    const previousList = this.conversations();

    // Newest pin first, which is where a pin just made belongs.
    this.pinnedConversations.set([conversation, ...previousPinned]);
    this.conversations.set(previousList.filter((c) => c.id !== conversationId));
    this.listError.set(null);

    try {
      await firstValueFrom(this.api.pinConversation(conversationId));
    } catch (error) {
      this.pinnedConversations.set(previousPinned);
      this.conversations.set(previousList);
      // ★ THE CAP HAS ITS OWN SENTENCE. "Could not pin" for a limit the user can actually do
      // something about (unpin one) would send them to look for a fault that is not there. The server
      // answers 422 with a translation key; anything else is an ordinary failure.
      this.listError.set(this.pinErrorKey(error));
    }
  }

  /**
   * Unpins it, putting it back where its last activity says it belongs.
   *
   * ★ IT GOES BACK INTO THE PAGED LIST IN ORDER, not at the end. The list is sorted by UpdatedAt
   * descending and the grouping reads it positionally; appending would file a conversation from this
   * morning under whatever band the last loaded row happens to be in.
   */
  async unpinConversation(conversationId: string): Promise<void> {
    const conversation = this.pinnedConversations().find((c) => c.id === conversationId);

    if (!conversation) {
      return;
    }

    const previousPinned = this.pinnedConversations();
    const previousList = this.conversations();

    this.pinnedConversations.set(previousPinned.filter((c) => c.id !== conversationId));
    this.conversations.set(AssistantStore.insertByRecency(previousList, conversation));
    this.listError.set(null);

    try {
      await firstValueFrom(this.api.unpinConversation(conversationId));
    } catch {
      this.pinnedConversations.set(previousPinned);
      this.conversations.set(previousList);
      this.listError.set('UNPIN_FAILED');
    }
  }

  /**
   * Puts a conversation back into the accumulated list at its place by last activity.
   *
   * ★ AND IT IS NOT APPENDED WHEN IT IS OLDER THAN EVERYTHING LOADED. A conversation older than
   * the last row on screen belongs in a batch that has not been fetched; putting it at the end would
   * show it above rows that come before it and, worse, would leave a copy behind when that batch does
   * arrive. Dropping it is honest: the server holds it, and "Load more" brings it back in its place.
   */
  private static insertByRecency(
    list: readonly AssistantConversationSummary[],
    conversation: AssistantConversationSummary,
  ): AssistantConversationSummary[] {
    const at = list.findIndex((c) => c.updatedAt < conversation.updatedAt);

    if (at === -1) {
      return [...list];
    }

    return [...list.slice(0, at), conversation, ...list.slice(at)];
  }

  /**
   * The 422 the server sends when the pinned group is full, or a plain failure.
   *
   * ★ THE PREFIX IS STRIPPED SO EVERY VALUE OF `listError` IS THE SAME SHAPE. The server sends a fully
   * qualified key ("ASSISTANT.PIN_LIMIT_REACHED") while the client's own failures are bare
   * ("LOAD_FAILED"), and the template prefixes what it renders — so a qualified key arriving unchanged
   * would render as "ASSISTANT.ASSISTANT.PIN_LIMIT_REACHED", i.e. as nothing.
   */
  private static readonly KEY_PREFIX = 'ASSISTANT.';

  private pinErrorKey(error: unknown): string {
    const key = (error as { error?: { messageKey?: string } } | null)?.error?.messageKey;

    if (typeof key !== 'string' || key.length === 0) {
      return 'PIN_FAILED';
    }

    return key.startsWith(AssistantStore.KEY_PREFIX)
      ? key.slice(AssistantStore.KEY_PREFIX.length)
      : key;
  }

  /** What to send as the search parameter: null when not searching, so the request omits it. */
  private effectiveSearch(): string | null {
    const term = this.searchTerm();
    return term.length >= AssistantStore.MIN_SEARCH_LENGTH ? term : null;
  }

  async startConversation(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const created = await firstValueFrom(this.api.startConversation());
      this.conversation.set(created);
      this.migrateNewConversationDraft(created.id);
      this.historyOpen.set(false);
      await this.loadConversations();
    } catch {
      this.error.set('START_FAILED');
    } finally {
      this.loading.set(false);
    }
  }

  async openConversation(conversationId: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.conversation.set(await firstValueFrom(this.api.getConversation(conversationId)));
      this.historyOpen.set(false);
    } catch {
      this.error.set('LOAD_FAILED');
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Sends a turn and renders the answer as it is written.
   *
   * Starts a thread first if there is none, so the user can just type — the empty panel should not make
   * them press "new conversation" before they are allowed to talk.
   *
   * ★ THE STREAMED TEXT IS NOT THE STORED MESSAGE. `streamingReply` holds what has arrived so far and
   * is rendered as a temporary bubble; when the `done` frame carries the persisted row, that row
   * replaces it. If the stream fails instead, the partial text is DISCARDED — the server stored nothing
   * for the assistant, and leaving half an answer on screen would show the user something that does not
   * exist and will not be there when they come back.
   */
  async send(content: string): Promise<void> {
    const trimmed = content.trim();
    if (trimmed.length === 0 || this.sending()) {
      return;
    }

    await this.exchange(trimmed, false);
  }

  /**
   * Ask the server to answer the last question AGAIN, after a failure.
   *
   * ★ IT DOES NOT RE-SEND THE MESSAGE. The backend commits the user's turn before it calls the model —
   * which is exactly what makes a failure survivable — so the question is already in the thread. Sending
   * it again would put the same words in the conversation twice, and the user would watch their own
   * message duplicate as the reward for pressing Retry. The retry flag tells the server to re-answer
   * what it already stored.
   *
   * The content is passed for the case where the turn NEVER reached the server (a transport failure
   * before the `user` frame); there is nothing stored to re-answer then, so it is a normal send.
   */
  async retry(): Promise<void> {
    // ★ ONE RETRY, TWO WAYS IN. The button under a failed turn and the one beside a stopped answer run
    // the SAME thing: re-answer the last stored question, without writing it again. They are kept as
    // separate signals only because they are separate SIGHTS — the failure card must not appear over a
    // turn the user stopped on purpose — and the two can never both be true, since a cancelled reply is
    // a stored answer and `retryable` needs the thread to be waiting on one.
    const pending = this.retryable() ?? this.retryableCancelled();
    if (!pending || this.sending()) {
      return;
    }

    await this.exchange(pending.content, pending.wasPersisted);
  }

  /**
   * The handle on the request in flight — what Stop actually pulls. See {@link AssistantStreamState}:
   * it lives with the conversation that owns the request, not beside the panel.
   *
   * ★ ABORTING THE FETCH IS THE WHOLE MECHANISM, on both sides. The browser stops reading, and the
   * server sees the connection go: its request token cancels, the call to the model is dropped mid
   * answer (no more tokens are paid for words nobody will read), and it writes what had arrived as a
   * cancelled turn. One signal, no second endpoint to keep in step with this one.
   *
   * ★ AND `cancelling` IS WHAT TELLS A CANCELLATION APART FROM A FAILURE, because they arrive
   * identically: aborting the fetch throws into the same catch a dead connection would. Without it the
   * user would press Stop and be told the assistant could not answer — blamed for a fault, and offered
   * a retry for a turn they deliberately ended.
   */

  /**
   * Stops the answer being written, and keeps what was written.
   *
   * ★ THE INPUT IS FREE IMMEDIATELY, which is the point of the button: someone presses Stop because
   * they want to ask something ELSE, so `sending` falls as the request unwinds and the composer is
   * usable before the stored row has even been read back.
   *
   * ★ THE CANCELLED TURN IS ON SCREEN BEFORE THE SERVER IS ASKED ANYTHING, and that is a correction of
   * how this worked at first. The original version froze the partial text and waited for the stored row
   * to read back before showing anything final — and the read LOST THE RACE: the server writes that row
   * while the aborted request unwinds, which is after the browser has already fired its question. Both
   * asks came back with the turn not yet stored, the frozen text was cleared as "it never existed", and
   * the user watched their answer VANISH on the click that was supposed to preserve it. Only a refresh
   * brought it back.
   *
   * So the order is inverted: the turn is written into the thread here, immediately, from the words that
   * were on screen — and the server's own row replaces it quietly when it lands. What is shown is never
   * invented; it is exactly the text the user just read, which is also exactly what the backend is in
   * the middle of storing.
   */
  async cancel(): Promise<void> {
    // ★ ONLY THE CONVERSATION ON SCREEN. Stop is a control the user is looking at, under the answer
    // they are watching; it has no business reaching into a turn being written somewhere else. Reading
    // "the request in flight" globally meant leaving a thread mid-answer and pressing Stop in another
    // one killed the first one's reply.
    const key = this.activeDraftKey();
    const entry = this.streams()[key];

    if (!entry?.sending || entry.controller === null) {
      return;
    }

    const conversationId = this.conversation()?.id ?? null;
    // Read BEFORE the abort: the exchange's own unwinding must not be racing us for this value.
    const partial = entry.reply;

    entry.controller.abort();

    // The controller is deliberately left in place: the exchange's `finally` compares against it to
    // tell "the turn I am cleaning up" from "a newer question already running".
    this.patchStream(key, { cancelling: true, steps: [], reply: null });

    // ★ SET HERE, SYNCHRONOUSLY, NOT LEFT TO THE EXCHANGE'S `finally`. That runs a few microtasks later,
    // once the aborted stream has finished throwing — so leaving it to that made "the composer is free"
    // depend on scheduling, and made the reconcile below unable to tell "the turn I just cancelled is
    // still unwinding" from "a new question is being answered". Pressing Stop IS the end of this turn;
    // saying so immediately is both truer and simpler.
    this.patchStream(key, { sending: false });

    // ★ NOTHING WRITTEN YET MEANS NOTHING TO KEEP. Stopping during the classifier or a lookup leaves
    // the server with an empty answer, and it stores nothing rather than a blank bubble — so there is
    // no row to wait for and no partial to show. The question simply stands unanswered, which is what
    // it is, and the composer is already free for the next one.
    if (partial === null || partial.length === 0) {
      return;
    }

    this.appendCancelledTurn(key, partial.trim());

    if (conversationId !== null && conversationId === key) {
      await this.reconcileCancelled(conversationId);
    }
  }

  /**
   * The id carried by the cancelled turn until the server's own row replaces it.
   *
   * A CONSTANT, not a generated id: there can only ever be one of these at a time — it is the answer
   * that was being written, and there is only one of those — and a stable value makes it obvious in a
   * debugger that this row is the local stand-in rather than something the backend sent.
   */
  static readonly PENDING_CANCELLED_ID = 'pending-cancelled';

  /**
   * Puts the stopped answer into the thread, as the turn it is.
   *
   * ★ IT GOES INTO `messages`, NOT BESIDE THEM. A separate "here is the cancelled bubble" signal would
   * render after every stored message, so the moment the user asked their next question — which is the
   * whole reason they pressed Stop — the stopped answer would jump BELOW it and the thread would read
   * out of order. Put in sequence, it is simply where it happened.
   */
  private appendCancelledTurn(key: string, content: string): void {
    const current = this.conversation();

    // ★ INTO THE CONVERSATION THAT WAS STOPPED, OR INTO NONE. `conversation` holds whatever is on
    // screen, which is not necessarily the thread this turn belongs to; the server has the row either
    // way, so a thread that is no longer open simply gets it back on the next read.
    if (!current || current.id !== key || content.length === 0) {
      return;
    }

    const lastSequence = current.messages.reduce((max, m) => Math.max(max, m.sequence), -1);

    this.conversation.set({
      ...current,
      messages: [
        ...current.messages,
        {
          id: AssistantStore.PENDING_CANCELLED_ID,
          role: 'Assistant',
          content,
          payload: null,
          sequence: lastSequence + 1,
          createdAt: new Date().toISOString(),
          status: 'Cancelled',
        },
      ],
      // The thread ends on an answer now, so it is not waiting on one. Without this the failure card
      // would appear beside a turn the user ended on purpose.
      lastTurnUnanswered: false,
    });
  }

  /**
   * How long to wait before each further ask. See {@link reconcileCancelled}.
   *
   * Backing off rather than hammering: the first gap covers the ordinary case (the write lands while
   * the request unwinds), and the later ones cover a server busy enough that it has not noticed the
   * dropped connection yet.
   */
  private static readonly RECONCILE_DELAYS_MS = [0, 300, 900, 2000];

  /**
   * Swaps the local stand-in for the row the server actually stored.
   *
   * ★ IT IS A CORRECTION, NEVER A REMOVAL. The stand-in is already correct — same words, same state —
   * so this exists only to replace it with the authoritative row (real id, server's truncation, server's
   * timestamp). If every ask comes back without it, the stand-in simply STAYS: taking it away would
   * reproduce the exact bug this ordering was changed to fix, and the next time the conversation is
   * opened the server's version is what renders anyway.
   */
  private async reconcileCancelled(conversationId: string): Promise<void> {
    for (const delay of AssistantStore.RECONCILE_DELAYS_MS) {
      if (delay > 0) {
        await new Promise((resolve) => setTimeout(resolve, delay));
      }

      let refreshed: AssistantConversation;
      try {
        refreshed = await firstValueFrom(this.api.getConversation(conversationId));
      } catch {
        return;
      }

      // The panel may have moved on while we were asking — a different thread, or this one already
      // busy answering something new. A snapshot taken before that started would erase it.
      if (
        this.conversation()?.id !== conversationId
        || this.streams()[conversationId]?.sending === true
      ) {
        return;
      }

      const last = [...refreshed.messages].sort((a, b) => a.sequence - b.sequence).at(-1);

      if (last && isCancelledReply(last)) {
        this.conversation.set(refreshed);
        await this.loadConversations();
        return;
      }
    }
  }

  /**
   * A failure THIS session saw, for the one case the server cannot report: the turn never reached it.
   *
   * When the request died before the `user` frame — no network, the tab lost connectivity — nothing was
   * stored, so there is no trailing question for the server to derive a failure from, and this is the
   * only record that one happened. It is session-only by nature: a refresh genuinely loses that turn,
   * because it never existed anywhere but here.
   */
  /**
   * The words of a turn that never reached the server, or null when there are none.
   *
   * ★ EXPOSED SO THE COMPOSER CAN HAND THEM BACK, and deliberately NOT so the thread can show them.
   * The user's turn belongs to the server — see the `user` frame — precisely so that a reload rebuilds
   * the conversation with no help from this browser. Putting an unsent message into `messages` would
   * create a second version of it that a refresh cannot find, which is the failure this design avoids.
   * The composer is the honest place for it: the message is not IN the conversation, it is still
   * something the user is about to say.
   */
  readonly unsentText = computed<string | null>(() => this.activeStream().unsent);

  /**
   * What a Retry would re-run, or null when there is nothing to retry — which is what hides the alert.
   *
   * ★ THE SERVER'S ANSWER WINS, AND THAT IS WHAT SURVIVES THE REFRESH. `lastTurnUnanswered` is derived
   * from the stored turns on every read, so a reloaded page reconstructs the failure with no help from
   * session memory. The local signal only covers the turn that never reached the server at all.
   */
  readonly retryable = computed<{ content: string; wasPersisted: boolean } | null>(() => {
    // ★ NOTHING TO OFFER WHILE ONE IS IN FLIGHT. A retry of a reloaded failure begins with the thread
    // still marked unanswered — it only stops being so when the answer lands — so without this the
    // card would sit next to the very loader that is resolving it, inviting a second retry of the
    // request already running.
    if (this.sending()) {
      return null;
    }

    // ★ THE EXACT RECORD BEATS THE INFERENCE, AND THAT ORDER IS THE SAFETY NET. `unsentFailure` holds
    // the very words that failed to send; the branch below INFERS a question from the stored thread.
    // Read the other way round — as it was — a lying `lastTurnUnanswered` shadowed the exact record
    // and the retry re-answered a different, older question while the user's own message was lost.
    // The flag is guarded now, but a local fact should never lose to a derivation regardless: if the
    // two ever disagree again, the one that cannot be wrong is this one.
    const unsent = this.activeStream().unsent;
    if (unsent !== null) {
      return { content: unsent, wasPersisted: false };
    }

    const conversation = this.conversation();

    if (conversation?.lastTurnUnanswered) {
      const lastQuestion = [...conversation.messages]
        .sort((a, b) => a.sequence - b.sequence)
        .reverse()
        .find((m) => m.role === 'User');

      if (lastQuestion) {
        return { content: lastQuestion.content, wasPersisted: true };
      }
    }

    return null;
  });

  /**
   * What the "Try again" beside a STOPPED answer would re-run, or null when there is no such offer.
   *
   * ★ ONLY WHEN THE STOPPED ANSWER IS THE LAST THING IN THE THREAD. Retrying re-answers the last stored
   * question, so on an older cancelled turn — one the user has since asked past — the button would say
   * "try again" and re-answer something else entirely. A control whose label describes a different
   * message than the one it sits under is worse than no control.
   *
   * ★ ALWAYS `wasPersisted: true`. There IS a stored assistant row here, which means the question that
   * produced it certainly reached the server. This can never be the never-arrived case.
   */
  readonly retryableCancelled = computed<{ content: string; wasPersisted: boolean } | null>(() => {
    // Nothing to offer while one is in flight — same rule as `retryable`, and for the same reason.
    if (this.sending()) {
      return null;
    }

    const ordered = [...(this.conversation()?.messages ?? [])].sort((a, b) => a.sequence - b.sequence);
    const last = ordered.at(-1);

    if (!last || !isCancelledReply(last)) {
      return null;
    }

    const question = [...ordered].reverse().find((m) => m.role === 'User');

    return question ? { content: question.content, wasPersisted: true } : null;
  });

  private async exchange(trimmed: string, isRetry: boolean): Promise<void> {
    // ★★ THE KEY IS TAKEN ONCE, HERE, AND EVERY WRITE BELOW GOES THROUGH IT. This single line is the
    // WI: from this point on the exchange never asks "which conversation is open" again. It knows
    // which conversation it is answering, and the user is free to go and read another one — the
    // fragments, the steps, the failure and the finished row all land where the question was asked.
    let key = this.activeDraftKey();

    this.error.set(null);
    // One controller per turn: an AbortController is single-use, so reusing last turn's would arrive
    // already fired and stop this request before it began.
    const controller = new AbortController();

    this.patchStream(key, {
      sending: true,
      errorKey: null,
      unsent: null,
      cancelling: false,
      controller,
      // Last turn's steps belong to last turn. Cleared here rather than at the end of the previous
      // exchange so a finished list is never left half-shown while the next request is being opened.
      steps: [],
      // ★ THE TYPING DOTS ARE NORMALLY LIT BY THE `user` FRAME — and a retry never sends one, because
      // the question is already stored and echoing it back would duplicate it on screen. So the retry
      // has to light them itself, or the user presses the button and watches nothing happen until the
      // first fragment lands: a button that looks broken at the exact moment it is working.
      //
      // Empty string, not null: the panel reads null as "nothing is streaming" and an empty reply as
      // "something is coming", which is precisely the state a retry starts in.
      reply: isRetry ? '' : null,
    });

    // Tracks whether the server got as far as storing the question. It decides whether a retry
    // re-answers the stored turn or sends a fresh one — get this wrong and the thread duplicates.
    let persisted = isRetry;

    try {
      if (key === AssistantStore.NEW_CONVERSATION_DRAFT) {
        const created = await firstValueFrom(this.api.startConversation());

        // ★ THE STREAM MIGRATES WITH THE DRAFT, for the same reason and at the same instant. The turn
        // is already marked as being answered under the new-conversation slot; the moment a real id
        // exists, everything that renders or stops this turn looks under that id instead.
        this.migrateStream(AssistantStore.NEW_CONVERSATION_DRAFT, created.id);
        this.migrateNewConversationDraft(created.id);
        key = created.id;

        // ★ ONLY IF THE USER IS STILL WHERE THEY WERE. Creating a thread takes a round trip, and
        // someone can open an old conversation inside it — showing the new one anyway would yank them
        // out of what they chose to read. The answer still lands in the thread that asked for it; the
        // list refresh at the end of this method is what makes it reachable.
        if (this.conversation() === null) {
          this.conversation.set(created);
        }
      }

      const conversationId = key;
      const token = this.auth.getAccessToken();

      for await (const frame of this.api.streamMessage(
        conversationId, trimmed, token, controller.signal, isRetry)) {
        switch (frame.type) {
          case 'user':
            persisted = true;
            // The SERVER's row for what we typed — never an optimistic local copy, which would be a
            // second version of the message that can drift from the stored one.
            //
            // ★ THE FLAG IS NOT TOUCHED HERE. The thread does technically end on an unanswered
            // question at this instant, but it is unanswered because the answer is ON ITS WAY — and
            // saying so put the failure card on screen beside the typing dots, telling the user their
            // question had failed while it was being answered. "Waiting" is not "failed"; only the
            // error frame below knows the difference.
            this.appendMessage(key, frame.message!);
            // ★★ THE TITLE ARRIVES WITH THE TURN THAT DECIDED IT. The server names a thread from
            // the first thing said in it, in the same write as the message and before the model is
            // called — so this is the moment both the header and the row can say the same thing.
            this.applyTitle(key, frame.title);
            this.patchStream(key, { reply: '' });
            break;

          case 'progress':
            this.recordStep(key, frame.phase, frame.state);
            break;

          case 'delta':
            this.appendDelta(key, frame.delta ?? '');
            break;

          case 'done':
            this.patchStream(key, { reply: null, steps: [], unsent: null });
            // ★ The answer landed, so the thread no longer ends on a question — the derived failure
            // clears itself. Marking the conversation answered here keeps the open screen honest
            // without a second round trip; a reload would compute the same thing.
            this.appendMessage(key, frame.message!, { threadUnanswered: false });
            break;

          case 'error':
            this.patchStream(key, {
              reply: null,
              // The steps go with the loader. Leaving a half-ticked checklist above a failure card
              // would show the user how far it got as if that were an outcome — nothing was persisted,
              // and the only thing to do with the turn is retry it.
              steps: [],
              errorKey: frame.errorKey ?? 'ASSISTANT.ERROR_UNAVAILABLE',
              unsent: persisted ? null : trimmed,
            });
            // NOW the thread is genuinely waiting on nothing — which is what the server will report
            // on the next read too. A stored question needs no local record beyond this flag; only
            // the turn that never arrived does.
            //
            // ★ ONLY IF THE QUESTION WAS ACTUALLY STORED. Setting this unconditionally made the flag
            // LIE: with nothing committed, the thread does not end on an unanswered question at all —
            // it ends wherever it ended before this attempt. And a lying flag was not a cosmetic
            // problem, because `retryable` trusts it: it went looking for the last stored User turn,
            // found the PREVIOUS, already-answered question, and offered to retry that. The user
            // watched their own message vanish and a second answer appear under the old one.
            this.markThreadUnanswered(key, persisted);
            this.restoreUnsentDraft(key, persisted ? null : trimmed);
            break;
        }
      }

      await this.loadConversations();
    } catch {
      // ★ A CANCELLATION LANDS HERE TOO — aborting the fetch throws exactly like a dead connection —
      // and it must take none of this. `cancel()` owns that path: it keeps the partial answer on
      // screen until the stored row replaces it, leaves the thread answered, and offers no retry,
      // because the user did not suffer a failure, they made a decision.
      //
      // ★ AND A CONVERSATION THAT WAS DELETED MID-ANSWER TAKES NONE OF IT EITHER. `remove` aborts the
      // request and drops the entry; writing a failure back here would resurrect state for a thread
      // that no longer exists, and the map would never go empty.
      const entry = this.streams()[key];

      if (entry !== undefined && !entry.cancelling) {
        this.patchStream(key, {
          reply: null,
          steps: [],
          errorKey: 'ASSISTANT.ERROR_UNAVAILABLE',
          unsent: persisted ? null : trimmed,
        });
        // Same guard as the error frame above, and it matters MORE here: a transport failure is the
        // likeliest way for a turn to die before the server ever saw it.
        this.markThreadUnanswered(key, persisted);
        this.restoreUnsentDraft(key, persisted ? null : trimmed);
      }
    } finally {
      // ★ ONLY IF THIS IS STILL THE TURN IN FLIGHT. A cancelled exchange frees the composer at the
      // click and then keeps unwinding for a few microtasks; the user can start a new question inside
      // that window, and this clean-up — belonging to a request that is already over — would otherwise
      // clear the NEW turn's sending flag and drop its abort handle, leaving a Stop button with nothing
      // to stop.
      if (this.streams()[key]?.controller === controller) {
        this.patchStream(key, { sending: false, controller: null, cancelling: false });
      }
    }
  }

  /**
   * Hands a turn that never reached the server back to the composer of the thread that asked it.
   *
   * ★ THE STORE DOES THIS, NOT THE COMPOSER, PRECISELY BECAUSE THE USER MAY HAVE LEFT. The component
   * can only restore into the box on screen; a question typed in A and lost while the user was reading
   * B has to go back into A's composer, and A's composer does not exist right now. The draft map is
   * where a conversation's unsent words live, and this is a conversation's unsent words.
   *
   * ★ NEVER OVER SOMETHING NEWER. If that thread's box already holds text, it is text the user typed
   * after this turn failed, and it is theirs.
   *
   * ★ AND NOT FOR THE THREAD ON SCREEN — the composer owns that case, because putting the words in the
   * draft map is only half of it: the textarea itself has to be filled and re-measured, which is DOM
   * the store cannot touch. Doing it here as well would fill the slot first and make the component
   * think there was nothing to hand back.
   */
  private restoreUnsentDraft(key: string, content: string | null): void {
    if (
      content === null
      || this.activeDraftKey() === key
      || (this.drafts()[key] ?? '').trim().length > 0
    ) {
      return;
    }

    this.setDraft(key, content);
  }

  /** Appends one fragment of the answer to the conversation that asked for it. */
  private appendDelta(key: string, delta: string): void {
    this.patchStream(key, { reply: (this.streams()[key]?.reply ?? '') + delta });
  }

  /**
   * Folds one `progress` frame into the visible list of steps.
   *
   * ★ A `done` FOR A STEP NOBODY ANNOUNCED IS STILL SHOWN, as an already-finished one. Frames can only
   * arrive in order over one stream, so this cannot really happen today — but the alternative, dropping
   * it, would silently lose a step the server did perform, and losing information is the one failure
   * mode this list must not have.
   */
  private recordStep(key: string, phase: string | undefined, state: string | undefined): void {
    if (!phase) {
      return;
    }

    const done = state === 'done';
    const steps = this.streams()[key]?.steps ?? [];
    const index = steps.findIndex((s) => s.phase === phase);

    if (index === -1) {
      this.patchStream(key, { steps: [...steps, { phase, done }] });
      return;
    }

    // A repeated `start` must not un-tick a step that already finished.
    if (!done) {
      return;
    }

    const next = [...steps];
    next[index] = { phase, done: true };
    this.patchStream(key, { steps: next });
  }

  /** Records that the thread is waiting on an answer that will not come. */
  /**
   * Records that the thread now ends on a question nobody answered.
   *
   * ★ THE PARAMETER IS THE POINT. It takes `persisted` rather than being called only when true,
   * because the caller reads better for it — and because a future error path that forgets the question
   * is a call that has to say, out loud, which of the two situations it is in. `false` is not "do
   * nothing by accident"; it is "the server holds no question, so the flag would be false anyway".
   */
  private markThreadUnanswered(key: string, persisted: boolean): void {
    const current = this.conversation();
    // Same rule as every other write in this file: the flag belongs to the thread that asked, and it
    // is only on screen if that thread is the one being read. A reload derives it from the server.
    if (current?.id === key && persisted) {
      this.conversation.set({ ...current, lastTurnUnanswered: true });
    }
  }

  /**
   * Writes a conversation's title everywhere it is shown.
   *
   * ★★ ONE WRITER, WHICH IS THE FIX. The title was reaching the two places at two different
   * times: the history list picked it up from its own refresh at the END of the exchange, while the
   * open conversation's header held the object fetched before the send and went on saying "New
   * conversation" indefinitely. The user watched the rail rename itself while the header did not.
   *
   * ★ AND THE TWO SIGNALS ARE NOT COLLAPSED INTO ONE, deliberately. `conversation` is the open
   * thread with its messages; `conversations` is a paged array of summaries that may not contain the
   * open thread at all — it can be pinned (returned outside the cursor), or sitting in a batch nobody
   * has loaded, or brand new. Deriving either from the other breaks exactly those cases. What is
   * single here is the WRITER: every projection of the title is updated from this method and nowhere
   * else, so they cannot disagree.
   */
  private applyTitle(conversationId: string, title: string | undefined): void {
    if (!title) {
      return;
    }

    const current = this.conversation();
    if (current?.id === conversationId && current.title !== title) {
      this.conversation.set({ ...current, title });
    }

    const rename = (list: AssistantConversationSummary[]) =>
      list.some((c) => c.id === conversationId && c.title !== title)
        ? list.map((c) => (c.id === conversationId ? { ...c, title } : c))
        : list;

    // ★ BOTH LISTS, because a pinned conversation is NOT in the paged one — the server excludes it
    // so it cannot render twice. Updating only `conversations` would leave a pinned thread showing its
    // old name in the group at the top.
    this.conversations.update(rename);
    this.pinnedConversations.update(rename);
  }

  /**
   * Appends one persisted row to the open conversation.
   *
   * `threadUnanswered` mirrors the rule the SERVER applies — a thread ending on a user turn is one
   * waiting for an answer — so the open screen and a reload of it cannot disagree. It is passed
   * explicitly rather than inferred from the row's role because the caller knows which frame it is
   * handling, and a rule guessed in two places is a rule that eventually differs in two places.
   */
  private appendMessage(
    key: string, message: AssistantMessage, options?: { threadUnanswered: boolean }): void {
    const current = this.conversation();

    // ★★ THE ROW GOES INTO THE THREAD THAT ASKED, OR NOWHERE. This guard is the other half of the WI,
    // and the half that was not merely cosmetic: `conversation` is whatever the user is reading, so
    // without it an answer about one payee's balance was appended to whichever conversation happened
    // to be open when it finished. Dropping it is safe and is why this can be a guard rather than a
    // second copy of the thread: the server stored the row against the id in the request URL, so
    // reopening that conversation reads it back complete.
    if (!current || current.id !== key) {
      return;
    }
    this.conversation.set({
      ...current,
      messages: [...current.messages, message],
      lastTurnUnanswered: options?.threadUnanswered ?? current.lastTurnUnanswered,
    });
  }

  async rename(conversationId: string, title: string): Promise<void> {
    const trimmed = title.trim();
    if (trimmed.length === 0) {
      return;
    }

    try {
      await firstValueFrom(this.api.renameConversation(conversationId, trimmed));
      const current = this.conversation();
      if (current?.id === conversationId) {
        this.conversation.set({ ...current, title: trimmed });
      }
      await this.loadConversations();
    } catch {
      this.error.set('RENAME_FAILED');
    }
  }

  async remove(conversationId: string): Promise<void> {
    // The thread is gone; its unsent text has nothing to belong to.
    this.clearDraft(conversationId);
    // ★ AND NEITHER DOES ITS ANSWER. A turn still being written for a conversation being deleted has
    // nowhere to land: the abort stops the model mid-sentence rather than paying for words that will
    // be thrown away, and dropping the entry is what stops a dead thread showing up as "busy" forever.
    this.abandonStream(conversationId);

    try {
      await firstValueFrom(this.api.deleteConversation(conversationId));
      if (this.conversation()?.id === conversationId) {
        this.conversation.set(null);
      }
      await this.loadConversations();
    } catch {
      this.error.set('DELETE_FAILED');
    }
  }
}
