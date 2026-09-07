import { AssistantMessage } from './assistant.model';

/** Mirrors ClarifyState on the server. */
export type ClarifyState = 'open' | 'answered' | 'dismissed';

/** What a form asks the user to choose between. Absent means the original: functions. */
export type ClarifyKind = 'function' | 'entity';

/**
 * One candidate record on an entity form.
 *
 * ★ THE STATUS IS A TOKEN, NOT A WORD. It arrives as `Active` / `Terminated` / `Draft` and is
 * translated here, for the same reason the function name is: the server must not put an English word
 * into a Spanish user's stored conversation (§C1).
 */
export interface ClarifyEntity {
  readonly name: string;
  readonly code: string | null;
  readonly status: string | null;
}

export interface ClarifyOption {
  /** A registered tool name. NEVER rendered raw — see {@link clarifyOptionKey}. */
  readonly function: string;
  /** What the user already said, or — on an entity form — the identifier that resolves one record. */
  readonly argument: string | null;
  /** The record this option stands for, when the choice is between records. */
  readonly entity?: ClarifyEntity | null;
}

export interface ClarifyForm {
  readonly options: readonly ClarifyOption[];
  readonly state: ClarifyState;
  readonly kind?: ClarifyKind;
  /** Set only when more records matched than are listed, so the panel can say so. */
  readonly totalMatches?: number | null;
}

/**
 * The translation whitelist for entity status tokens.
 *
 * ★ SAME RULE AS THE FUNCTION MAP AND FOR THE SAME REASON (§C2): no key is built by concatenation, so
 * a status this build has never heard of renders as NOTHING rather than as a raw token. The option
 * still shows its name and code, which are already enough to tell two records apart — degrading to
 * less information is fine, printing `OnLeave` at a user is not.
 */
const STATUS_KEYS: Readonly<Record<string, string>> = {
  Active: 'ASSISTANT.CLARIFY.STATUS_ACTIVE',
  OnLeave: 'ASSISTANT.CLARIFY.STATUS_ON_LEAVE',
  Terminated: 'ASSISTANT.CLARIFY.STATUS_TERMINATED',
  Draft: 'ASSISTANT.CLARIFY.STATUS_DRAFT',
  Archived: 'ASSISTANT.CLARIFY.STATUS_ARCHIVED',
};

/**
 * The translation whitelist for the functions a clarify form may offer.
 *
 * ★★ THE KEY IS NEVER BUILT BY CONCATENATION (§C2). `'ASSISTANT.CLARIFY.' + option.function` would
 * put `get_payee_balance` on screen the first time the server offered a function this build had never
 * heard of — an internal identifier in front of a user, which rule 10a already forbids the assistant
 * itself from doing. An explicit map cannot: an unknown function is DROPPED rather than rendered,
 * because a button whose label you cannot write is a button whose behaviour you cannot promise.
 */
const OPTION_KEYS: Readonly<Record<string, string>> = {
  get_transaction: 'ASSISTANT.CLARIFY.OPTION_TRANSACTION',
  get_plan_rules: 'ASSISTANT.CLARIFY.OPTION_PLAN_RULES',
  get_payee_balance: 'ASSISTANT.CLARIFY.OPTION_PAYEE_BALANCE',
  get_payee_plans: 'ASSISTANT.CLARIFY.OPTION_PAYEE_PLANS',
  simulate_plan_rules: 'ASSISTANT.CLARIFY.OPTION_SIMULATE',
};

/**
 * What the user's message should say when they press an option.
 *
 * ★★ PRESSING A BUTTON SENDS A SENTENCE, IT DOES NOT CALL THE TOOL DIRECTLY. The client has no route
 * to a tool and must not have one: the dispatcher picks the tool, and a client that could name it
 * would be a second, competing dispatcher — with none of the prompt's rules about which identifier
 * goes to which lookup. So the option turns into the question the user would have typed, and the
 * ordinary turn machinery does the rest, including the anti-invention guard.
 */
const OPTION_PROMPT_KEYS: Readonly<Record<string, string>> = {
  get_transaction: 'ASSISTANT.CLARIFY.ASK_TRANSACTION',
  get_plan_rules: 'ASSISTANT.CLARIFY.ASK_PLAN_RULES',
  get_payee_balance: 'ASSISTANT.CLARIFY.ASK_PAYEE_BALANCE',
  get_payee_plans: 'ASSISTANT.CLARIFY.ASK_PAYEE_PLANS',
  simulate_plan_rules: 'ASSISTANT.CLARIFY.ASK_SIMULATE',
};

function own(map: Readonly<Record<string, string>>, key: string): string | null {
  // Own properties only: a plain object answers `toString` and `constructor` from its prototype, so
  // `?? fallback` would not fire and a function called "constructor" would render a native function.
  return Object.prototype.hasOwnProperty.call(map, key) ? map[key] : null;
}

/** The label key for an option, or null when this build cannot name that function. */
export function clarifyOptionKey(fn: string | null | undefined): string | null {
  return fn ? own(OPTION_KEYS, fn) : null;
}

/** The key for the sentence pressing that option sends. */
export function clarifyPromptKey(fn: string | null | undefined): string | null {
  return fn ? own(OPTION_PROMPT_KEYS, fn) : null;
}

/** The label key for a status token, or null when this build cannot name it. */
export function clarifyStatusKey(status: string | null | undefined): string | null {
  return status ? own(STATUS_KEYS, status) : null;
}

/**
 * The record an option stands for, or null when the option is a function rather than a record.
 *
 * ★ A NAME IS REQUIRED AND THE REST IS NOT. Without a name there is nothing to render, so the option
 * falls back to being labelled by its function — which is always safe, because every option carries a
 * whitelisted function name whatever else it has.
 */
export function clarifyEntityOf(option: ClarifyOption): ClarifyEntity | null {
  const entity = option.entity;
  return entity && typeof entity.name === 'string' && entity.name.length > 0 ? entity : null;
}

/**
 * Reads the clarify form out of a message's payload.
 *
 * ★ THE PAYLOAD IS SHARED GROUND. It also carries `resolvedEntities`, so this reads ONE key and
 * ignores everything else rather than assuming the object is its own.
 *
 * ★ AND IT NEVER THROWS. The payload is server-controlled text; a parse failure means "no form",
 * which renders as no panel — the conversation is still perfectly readable without it.
 */
export function clarifyOf(message: AssistantMessage): ClarifyForm | null {
  if (!message.payload) return null;

  try {
    const parsed = JSON.parse(message.payload) as { clarify?: ClarifyForm };
    const form = parsed?.clarify;

    if (!form || !Array.isArray(form.options)) return null;

    // Options this build cannot label are dropped, not rendered raw. An ENTITY option survives this
    // for the same reason a function one does: it still names a real, whitelisted function — the
    // record's own label is extra, never the thing that makes it renderable.
    const options = form.options.filter((o) => o && clarifyOptionKey(o.function) !== null);

    if (options.length === 0) return null;

    return {
      options,
      state: form.state ?? 'open',
      kind: form.kind === 'entity' ? 'entity' : 'function',
      // ★ ONLY MEANINGFUL WHEN IT EXCEEDS WHAT IS SHOWN. Rendering "1 of 1" would be noise, and
      // rendering a number the server did not send would be worse.
      totalMatches:
        typeof form.totalMatches === 'number' && form.totalMatches > options.length
          ? form.totalMatches
          : null,
    };
  } catch {
    return null;
  }
}

/**
 * The form the composer should show, or null.
 *
 * ★★ ONLY AN `open` FORM IS SHOWN, AND THAT IS THE TICKET'S THIRD COMMENT AS A PREDICATE: a form the
 * user answered or closed must NOT reappear fresh after a refresh. Its state lives on the message, so
 * "already dealt with" survives a reload without the browser remembering anything.
 *
 * ★ THE LAST ONE WINS. A long thread can hold several forms over its life; the one worth acting on is
 * the most recent still-open one, and older ones are history that was already resolved.
 */
export function openClarify(
  messages: readonly AssistantMessage[],
): { message: AssistantMessage; form: ClarifyForm } | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const message = messages[i];
    if (message.role !== 'Assistant') continue;

    const form = clarifyOf(message);
    if (form?.state === 'open') return { message, form };
  }

  return null;
}
