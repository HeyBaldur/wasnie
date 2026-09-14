import { CalculatePayRunResult } from './pay-run.model';

/**
 * How a pay run explains itself: the skip-reason whitelist and the "nothing was created" headline.
 *
 * ★ ONE COPY, TWO SCREENS. This lived in the pay runs list; the guided tour's sandbox runs the same
 * command and has to say the same thing about the same result. A second copy of the whitelist would
 * drift the first time the engine gained a code, and the sandbox would start explaining a pay run
 * differently from the product it is meant to rehearse.
 */

/**
 * The reason codes this version knows how to phrase.
 *
 * ★ A WHITELIST, NOT A STRING CONCATENATION. Building `PAY_RUNS.SKIP_${code}` blindly would print the
 * raw key — an internal identifier — the first time the backend adds a code the front end has not
 * shipped a translation for. An unknown code degrades to a neutral line instead: it still reports
 * that something was skipped, and it does NOT guess why.
 */
const KNOWN_SKIP_CODES: readonly string[] = [
  'TerminatedPayee',
  'PlanNotPayable',
  'ExistingPayout',
  // Not skips — the run explaining itself. They share this list because they travel in the same
  // field, and leaving them out would print the neutral fallback over a sentence the engine had.
  'SupplementalForNewCredits',
  'UnreachableCommission',
];

export function payRunSkipLabelKey(code: string): string {
  return KNOWN_SKIP_CODES.includes(code) ? `PAY_RUNS.SKIP_${code}` : 'PAY_RUNS.SKIP_UNKNOWN';
}

/**
 * Did the run leave behind commission no pay run can reach? Read off the engine's own code, never
 * inferred from a zero — a zero has several causes and this is only one of them.
 */
export function hasUnreachableCommission(result: CalculatePayRunResult): boolean {
  return (result.diagnostics?.skipped ?? []).some(s => s.code === 'UnreachableCommission' && s.count > 0);
}

/**
 * The headline for a run that produced nothing. Distinct answers, because they are distinct
 * situations: commission no run can reach, nothing to consider, everything discarded (reasons listed
 * underneath), and the neutral fallback, which claims no cause whatsoever.
 */
export function noPayoutsHeadlineKey(result: CalculatePayRunResult): string {
  const d = result.diagnostics;
  if (!d) return 'PAY_RUNS.CALCULATE_NO_PAYOUTS_NEUTRAL';
  if (hasUnreachableCommission(result)) return 'PAY_RUNS.CALCULATE_UNREACHABLE';
  if (d.assignmentsConsidered === 0) return 'PAY_RUNS.CALCULATE_NOTHING_TO_CONSIDER';
  if (d.skipped.length > 0) return 'PAY_RUNS.CALCULATE_ALL_SKIPPED';
  return 'PAY_RUNS.CALCULATE_NO_PAYOUTS_NEUTRAL';
}
