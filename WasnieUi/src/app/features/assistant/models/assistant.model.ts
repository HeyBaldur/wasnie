export type AssistantMessageRole = 'User' | 'Assistant';

/**
 * How a stored turn ENDED.
 *
 * `Cancelled` means the user stopped the answer while it was being written: the words that had already
 * arrived are stored — they were on screen and deleting them would erase what the user read — and this
 * is what stops them from passing for a finished reply. It comes from the SERVER on every read, which
 * is why a cancellation is still marked after a reload.
 */
export type AssistantMessageStatus = 'Complete' | 'Cancelled';

/**
 * One turn.
 *
 * `payload` is ALWAYS null in this piece. It is in the contract from day one because later pieces
 * attach structure to a turn (retrieved document references, screen context, the JSON that pre-fills
 * a Plan+Quota form) — a field that appears later is a breaking change for every consumer, a field
 * that is always null is not.
 */
export interface AssistantMessage {
  id: string;
  role: AssistantMessageRole;
  content: string;
  payload: string | null;
  sequence: number;
  createdAt: string;

  /**
   * How the turn ended. Optional in the type only so a response from a backend older than this field
   * still parses; absent is read as `Complete` by {@link isCancelledReply}, which is what every turn
   * written before cancelling existed actually was.
   */
  status?: AssistantMessageStatus;
}

export interface AssistantConversationSummary {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
}

/**
 * One BATCH of the history list, and where the next one starts.
 *
 * ★ THE CURSOR IS OPAQUE AND THE CLIENT NEVER BUILDS ONE. It is echoed back exactly as received, so
 * the server can change what a cursor is made of — a pin flag, a different sort — without this file
 * knowing. `null` means there is nothing more, which is how the Load more button knows to disappear
 * instead of comparing counts to a page size and getting it wrong on an exact multiple.
 */
export interface AssistantConversationPage {
  items: AssistantConversationSummary[];
  nextCursor: string | null;
  /**
   * The caller's pinned conversations, complete and newest-pin-first.
   *
   * ★★ OUTSIDE THE CURSOR, WHICH IS THE WHOLE POINT. Pinned threads are exactly the OLD ones — the
   * ones that have sunk far past the first batch — so putting them through the cursor would mean an old
   * pin simply is not in the first response, which is the problem pinning exists to solve. Sent with
   * the FIRST batch only; a continuation already has the group on screen.
   */
  pinned: AssistantConversationSummary[];
}

export interface AssistantConversation {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messages: AssistantMessage[];

  /**
   * True when the thread ends on a question the assistant never answered.
   *
   * ★ THIS IS WHAT SURVIVES A REFRESH. The server derives it from the stored turns on every read — the
   * question is committed before the model is called, and the reply only after it succeeds, so a
   * trailing user turn IS the record of the failure. Nothing about it lives in this browser, which is
   * exactly why reloading no longer loses it.
   */
  lastTurnUnanswered: boolean;
}

export interface AssistantExchange {
  userMessage: AssistantMessage;
  assistantMessage: AssistantMessage;
}

export interface AssistantEntitlement {
  enabled: boolean;
  /**
   * True when the user holds the seat but the WORKSPACE is on Free — the one refusal the UI shows
   * instead of hiding, because upgrading is something this user can actually go and do. Every other
   * "no" stays invisible: offering an upgrade to someone a bigger plan would not help is a lie.
   */
  requiresUpgrade: boolean;
}

/**
 * One frame of a streamed exchange.
 *
 * `errorKey` is a TRANSLATION KEY, never a sentence and never the model vendor's own words — the
 * backend translates provider failures into keys precisely so nothing operational reaches the reader.
 */
export interface AssistantStreamEvent {
  type: 'user' | 'delta' | 'done' | 'error' | 'progress';
  delta?: string;
  message?: AssistantMessage;
  errorKey?: string;

  /**
   * Which step of the turn a `progress` frame is about — an IDENTIFIER, never a sentence, translated
   * here for the same reason `errorKey` is: the reader gets their own language.
   */
  phase?: string;
  /** The conversation's title after this turn. Only on `user` frames — see the store. */
  title?: string;

  /** `'start'` or `'done'` on a `progress` frame. */
  state?: string;
}

/**
 * One step of the turn, as the panel renders it.
 *
 * ★ THE LIST IS BUILT FROM WHAT THE SERVER REPORTS, NEVER FROM A SCRIPT. The backend emits a step only
 * when it really performs that work, so a documentation question shows no database search and a
 * question the guide does not cover shows no documentation step. Pre-seeding the list with the steps a
 * turn "usually" takes would put a tick beside work nobody did — and this panel's whole design is that
 * it never claims what it cannot verify.
 */
export interface AssistantProgressStep {
  phase: string;
  done: boolean;
}

/** The steps this client has wording for. Anything else is still shown, under a neutral label. */
const KNOWN_PHASES = new Set(['understanding', 'reading_docs', 'searching_data', 'generating']);

/**
 * The translation key for a phase.
 *
 * ★ AN UNKNOWN PHASE IS RENDERED, NOT DROPPED. A newer backend may report a step this build has never
 * heard of; showing it as "Working…" is honest — something IS happening — while hiding it would make
 * the panel look stalled during the very step it was told about.
 */
export function phaseLabelKey(phase: string): string {
  return KNOWN_PHASES.has(phase)
    ? `ASSISTANT.PHASE.${phase.toUpperCase()}`
    : 'ASSISTANT.PHASE.WORKING';
}

/**
 * The content the backend stores for the assistant's stand-in reply while no model is connected.
 *
 * A SENTINEL, not a sentence — it is stored language-neutral precisely so this client can render it
 * in the reader's language. Must stay byte-identical to `AssistantMessage.NotConnectedPlaceholder`
 * in the backend; when a real model answers, its words are stored instead and this stops matching
 * new rows while old rows keep rendering as the translated placeholder.
 */
export const ASSISTANT_NOT_CONNECTED = '__ASSISTANT_NOT_CONNECTED__';

/**
 * What the backend stores as the title of a thread nothing has been said in yet.
 *
 * A SENTINEL, not a sentence, for the same reason as the stand-in reply: the history list is read by
 * its owner in their own language, and freezing "New conversation" in English into a row would show it
 * that way forever. Must stay byte-identical to `AssistantConversation.UntitledSentinel`.
 */
export const ASSISTANT_UNTITLED = '__UNTITLED__';

export function isUntitled(title: string | null | undefined): boolean {
  return !title || title === ASSISTANT_UNTITLED;
}

export function isPlaceholderReply(message: AssistantMessage): boolean {
  return message.role === 'Assistant' && message.content === ASSISTANT_NOT_CONNECTED;
}

/**
 * True when this row is an answer the user stopped mid-write.
 *
 * ★ READ FROM THE ROW, NEVER FROM SESSION MEMORY. That is the whole reason the status is stored: the
 * notice under a cancelled answer must be there for someone who reopens the conversation tomorrow, in
 * a browser that never saw the click.
 */
export function isCancelledReply(message: AssistantMessage): boolean {
  return message.role === 'Assistant' && message.status === 'Cancelled';
}

/**
 * The route inside this app that an `href` points at, or null when it points somewhere else.
 *
 * ★ ONE DEFINITION, USED BY BOTH SIDES. The Markdown pipe asks this to decide whether a link gets
 * `target="_blank"`, and the panel asks it to decide whether to intercept the click. If the two ever
 * disagreed, a link would be opened in a new tab AND routed internally, or given neither treatment —
 * so there is exactly one answer to "is this internal?" and both callers read it.
 *
 * ★ `//` IS NOT INTERNAL, and this is the trap the naive check falls into. `//evil.com` starts with a
 * slash and is a PROTOCOL-RELATIVE URL: the browser resolves it to `https://evil.com`. Treating it as a
 * route would hand a model-authored destination to the app's own router. A single leading slash, and
 * only that, means "a path in this application".
 */
export function internalRouteOf(href: string | null | undefined): string | null {
  if (!href || !href.startsWith('/') || href.startsWith('//')) {
    return null;
  }

  return href;
}
