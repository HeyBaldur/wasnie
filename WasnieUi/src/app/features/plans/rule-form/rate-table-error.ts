import { ApiErrorCode } from '../../../shared/utils/api-error';

/**
 * The bound codes the server sends alongside a ladder refusal. They say what the tier bounds are
 * denominated in, which is the whole point of two of the six messages: the mistake this validation
 * catches is a unit mix-up, and a sentence that does not name the unit does not help the person who
 * made it.
 */
const BOUND_AMOUNT = 'Amount';
const BOUND_ATTAINMENT_RATIO = 'AttainmentRatio';

/** What to say when the server refused a rate table for a reason this version does not know. */
export const RATE_TABLE_ERR_UNKNOWN = 'PLANS.RATE_TABLE_ERR_UNKNOWN';

/**
 * Turns one coded rate-table refusal into the translation key that phrases it.
 *
 * ★★ AN EXPLICIT WHITELIST, NEVER A CONCATENATION. Writing `PLANS.RATE_TABLE_ERR_${code}` would work
 * for exactly as long as the two sides agree, and the first time the backend adds an invariant this
 * build has no translation for, the user is shown a raw internal identifier — "RateTableTiersFooBar"
 * — in place of a sentence. Every branch below returns a LITERAL key, so an unrecognised code cannot
 * produce one: it falls to the generic line, which still says the table was rejected and does not
 * pretend to know why. This mirrors `skipLabelKey` in the pay-runs list.
 *
 * ★ THE TWO UNIT-SENSITIVE CODES GET TWO KEYS EACH, rather than one key with a translated noun
 * dropped into it. "amount" and "attainment ratio" decline differently in Polish and take different
 * articles in Spanish, so a sentence assembled from a translated word is a sentence that reads wrong
 * in two of the three languages. The unknown bound falls back to the amount wording — the ladder is
 * still described correctly, only the unit is generic.
 */
export function rateTableErrorKey(error: ApiErrorCode): string {
  const bound = error.parameters['bound'];

  switch (error.code) {
    case 'RateTableEmpty':
      return 'PLANS.RATE_TABLE_ERR_EMPTY';

    case 'RateTableTiersOutOfOrder':
      return 'PLANS.RATE_TABLE_ERR_OUT_OF_ORDER';

    case 'RateTableNonLastTierMustBeClosed':
      return 'PLANS.RATE_TABLE_ERR_NON_LAST_OPEN';

    case 'RateTableLastTierMustBeOpen':
      return bound === BOUND_ATTAINMENT_RATIO
        ? 'PLANS.RATE_TABLE_ERR_LAST_BOUNDED_RATIO'
        : 'PLANS.RATE_TABLE_ERR_LAST_BOUNDED_AMOUNT';

    case 'RateTableTiersOverlap':
      return 'PLANS.RATE_TABLE_ERR_OVERLAP';

    case 'RateTableTiersLeaveGap':
      return bound === BOUND_ATTAINMENT_RATIO
        ? 'PLANS.RATE_TABLE_ERR_GAP_RATIO'
        : 'PLANS.RATE_TABLE_ERR_GAP_AMOUNT';

    default:
      return RATE_TABLE_ERR_UNKNOWN;
  }
}

/**
 * True when this build knows how to phrase the code. Kept separate so the caller can decide to fall
 * back to the plain `message` path rather than to the generic rate-table line — a coded 422 from some
 * other endpoint is not a rate-table problem and must not be described as one.
 */
export function isKnownRateTableError(error: ApiErrorCode): boolean {
  return rateTableErrorKey(error) !== RATE_TABLE_ERR_UNKNOWN;
}

/**
 * The values the sentence interpolates.
 *
 * ★ THE NUMBERS ARE NOT REFORMATTED. They are echoed back into the very fields the user typed them
 * into, so `0.7999` must read as `0.7999` — running it through a locale formatter would show the
 * reader a number that does not appear anywhere on their screen. `bound` is dropped: it chose the
 * key, it is not part of any sentence.
 */
export function rateTableErrorParams(error: ApiErrorCode): Record<string, unknown> {
  const { bound: _bound, ...rest } = error.parameters;
  return rest;
}
