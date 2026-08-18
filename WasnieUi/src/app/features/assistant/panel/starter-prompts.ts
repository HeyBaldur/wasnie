/**
 * The example questions offered under the welcome, and the one rule that governs them.
 *
 * ★★ EVERY STARTER MAPS TO A TOOL THAT EXISTS. The welcome has always lived under a hard rule — it
 * offers nothing the assistant cannot do — and these buttons do not relax it, they inherit it. There
 * are exactly four read-only tools on the server (`get_payee_ledger_summary`, `get_payee_plans`,
 * `get_transaction`, `get_plan_rules`) and exactly four starters, one each. The `tool` field below is
 * not documentation: a test reads it and fails if a starter ever names a tool the backend does not
 * register.
 *
 * ★ WHY THAT MATTERS MORE HERE THAN ANYWHERE ELSE ON THE SCREEN. A suggested question is read as a
 * promise the product makes about itself. "Why is my commission negative?" would be a reasonable-looking
 * chip and there is no tool behind it — the user would click it, get a refusal, and learn that the
 * assistant does not work. An empty promise offered by the product is worse than no suggestion at all.
 *
 * ★ AND NONE OF THEM SENDS. Each fills the composer with a sentence containing a placeholder the user
 * has to replace with a real name, reference or plan. Sending on click would fire a lookup for the
 * literal text "[payee name]", which resolves to nobody — the first thing the user would see is the
 * assistant failing to find a record.
 */
export interface StarterPrompt {
  /** Short text on the button. */
  readonly labelKey: string;

  /** The sentence dropped into the composer. Contains the bracketed placeholder. */
  readonly promptKey: string;

  /** The backend tool this question reaches. Asserted against the real tool list by a test. */
  readonly tool: string;

  readonly testId: string;
}

export const STARTER_PROMPTS: readonly StarterPrompt[] = [
  {
    labelKey: 'ASSISTANT.STARTER_BALANCE_LABEL',
    promptKey: 'ASSISTANT.STARTER_BALANCE_PROMPT',
    tool: 'get_payee_ledger_summary',
    testId: 'assistant-starter-balance',
  },
  {
    labelKey: 'ASSISTANT.STARTER_PLANS_LABEL',
    promptKey: 'ASSISTANT.STARTER_PLANS_PROMPT',
    tool: 'get_payee_plans',
    testId: 'assistant-starter-plans',
  },
  {
    labelKey: 'ASSISTANT.STARTER_TRANSACTION_LABEL',
    promptKey: 'ASSISTANT.STARTER_TRANSACTION_PROMPT',
    tool: 'get_transaction',
    testId: 'assistant-starter-transaction',
  },
  {
    labelKey: 'ASSISTANT.STARTER_PLAN_RULES_LABEL',
    promptKey: 'ASSISTANT.STARTER_PLAN_RULES_PROMPT',
    tool: 'get_plan_rules',
    testId: 'assistant-starter-plan-rules',
  },
];

/**
 * Where the placeholder sits in a filled-in prompt, so the composer can select it.
 *
 * ★ FOUND BY BRACKETS, NOT BY A PER-LANGUAGE INDEX. "[payee name]", "[nombre del payee]" and
 * "[imię i nazwisko]" are different lengths in different places, and hard-coding offsets would mean
 * three sets of numbers that silently rot the first time a translator rephrases a sentence. The
 * brackets travel with the text, so the range is derived from whatever the translation actually says.
 *
 * Returns the caret at the END of the text when the sentence carries no placeholder — a translation
 * that dropped the brackets still puts the user in a usable box rather than selecting nothing at
 * position zero and swallowing their first keystroke.
 */
export function placeholderRange(text: string): { start: number; end: number } {
  const open = text.indexOf('[');
  const close = open === -1 ? -1 : text.indexOf(']', open + 1);

  return open === -1 || close === -1
    ? { start: text.length, end: text.length }
    : { start: open, end: close + 1 };
}
