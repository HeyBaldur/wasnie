/**
 * What kind of failure a turn hit, and therefore what the user can do about it.
 *
 * ★★ "TRY AGAIN" IS ONLY OFFERED WHEN TRYING AGAIN CAN WORK. It used to be shown for every failure — including a
 * trial whose assistant tokens are used up, where pressing it a thousand times changes nothing: the server refuses
 * the turn before the model is ever called. A button that cannot succeed is not a way out, it is a trap.
 *
 * ★ AN EXPLICIT WHITELIST OF THE NON-RETRYABLE CODES (§C2). Anything not named here — a transient provider failure,
 * a rate limit, an unknown future code, or no code at all after a reload — stays retryable, because for those a
 * retry is the right first move and hiding it would strand the question.
 */
export type AssistantFailureKind =
  /** The trial's assistant tokens are used up: the way forward is to subscribe, not to retry. */
  | 'trialLimit'
  /** A PAYING tenant spent its included tokens and its boost: the way forward is to buy a boost, not to retry. */
  | 'tokensExhausted'
  /** No model is configured for this environment: nothing the user does here will change that. */
  | 'notConfigured'
  /** Everything else: the provider was busy or failed, and a retry can succeed. */
  | 'retryable';

export const TRIAL_LIMIT_KEY = 'ASSISTANT.ERROR_TRIAL_LIMIT_REACHED';
export const TOKENS_EXHAUSTED_KEY = 'ASSISTANT.ERROR_TOKENS_EXHAUSTED';
export const NOT_CONFIGURED_KEY = 'ASSISTANT.ERROR_NOT_CONFIGURED';

export function assistantFailureKind(errorKey: string | null | undefined): AssistantFailureKind {
  switch (errorKey) {
    case TRIAL_LIMIT_KEY:
      return 'trialLimit';
    case TOKENS_EXHAUSTED_KEY:
      return 'tokensExhausted';
    case NOT_CONFIGURED_KEY:
      return 'notConfigured';
    default:
      return 'retryable';
  }
}
