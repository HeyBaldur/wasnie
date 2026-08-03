export type AssistantMessageRole = 'User' | 'Assistant';

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
}

export interface AssistantConversationSummary {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
}

export interface AssistantConversation {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messages: AssistantMessage[];
}

export interface AssistantExchange {
  userMessage: AssistantMessage;
  assistantMessage: AssistantMessage;
}

export interface AssistantEntitlement {
  enabled: boolean;
}

/**
 * One frame of a streamed exchange.
 *
 * `errorKey` is a TRANSLATION KEY, never a sentence and never the model vendor's own words — the
 * backend translates provider failures into keys precisely so nothing operational reaches the reader.
 */
export interface AssistantStreamEvent {
  type: 'user' | 'delta' | 'done' | 'error';
  delta?: string;
  message?: AssistantMessage;
  errorKey?: string;
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
