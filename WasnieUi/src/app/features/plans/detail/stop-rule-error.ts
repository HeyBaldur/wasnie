import { ApiErrorCode } from '../../../shared/utils/api-error';

/** What to say when the server refused a stop for a reason this build does not know. */
export const STOP_RULE_ERR_UNKNOWN = 'PLANS.STOP_RULE_ERR_UNKNOWN';

/**
 * Turns one coded refusal from the stop endpoint into the translation key that phrases it.
 *
 * ★★ AN EXPLICIT WHITELIST, NEVER A CONCATENATION. Writing `PLANS.STOP_RULE_ERR_${code}` works for
 * exactly as long as the two sides agree, and the first time the backend adds an invariant this
 * build has no translation for, the user is shown a raw internal identifier — "RuleStopFooBar" — in
 * place of a sentence. Every branch returns a LITERAL key, so an unrecognised code cannot produce
 * one: it falls to the generic line, which says the rule was not stopped without pretending to know
 * why. Same shape as `rateTableErrorKey` and `skipLabelKey`.
 *
 * ★ AND IT MATTERS MORE HERE THAN ANYWHERE. This dialog gets opened because money is going out
 * wrong. Someone who cannot read the refusal will retry it, or route around it.
 */
export function stopRuleErrorKey(error: ApiErrorCode): string {
  switch (error.code) {
    case 'RuleAlreadyStopped':
      return 'PLANS.STOP_RULE_ERR_ALREADY_STOPPED';

    case 'RuleStopReasonRequired':
      return 'PLANS.STOP_RULE_ERR_REASON_REQUIRED';

    case 'RuleStopReasonTooLong':
      return 'PLANS.STOP_RULE_ERR_REASON_TOO_LONG';

    case 'RuleStopPlanNotActive':
      return 'PLANS.STOP_RULE_ERR_PLAN_NOT_ACTIVE';

    case 'RuleStopRuleNotFound':
      return 'PLANS.STOP_RULE_ERR_RULE_NOT_FOUND';

    default:
      return STOP_RULE_ERR_UNKNOWN;
  }
}

/**
 * The values the sentence interpolates. Numbers stay numbers — `maxLength` is a character count the
 * reader is about to compare against their own text, not a figure to localise.
 */
export function stopRuleErrorParams(error: ApiErrorCode): Record<string, unknown> {
  return error.parameters;
}
