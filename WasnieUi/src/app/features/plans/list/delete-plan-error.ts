import { ApiErrorCode } from '../../../shared/utils/api-error';

/** What to say when the server refused a delete for a reason this build does not know. */
export const DELETE_PLAN_ERR_UNKNOWN = 'PLANS.DELETE_PLAN_ERR_UNKNOWN';

/**
 * Turns one coded refusal from `DELETE /api/plans/{id}` into the translation key that phrases it (KAN-69).
 *
 * ★★ AN EXPLICIT WHITELIST, NEVER A CONCATENATION (§C2). Every branch returns a LITERAL key, so a code
 * this build has never heard of falls to the generic line instead of printing an internal identifier.
 * Same shape as `stopRuleErrorKey` and `rateTableErrorKey`.
 *
 * ★ THE MENU ALREADY HIDES DELETE WHEN THE SERVER WOULD REFUSE (`isDeletable`), so reaching these means
 * the plan changed between loading the list and confirming — an assignment created meanwhile, say.
 */
export function deletePlanErrorKey(error: ApiErrorCode): string {
  switch (error.code) {
    case 'PlanDeleteNotDraft':
      return 'PLANS.DELETE_PLAN_ERR_NOT_DRAFT';

    case 'PlanDeleteHasDependencies':
      return 'PLANS.DELETE_PLAN_ERR_HAS_DEPENDENCIES';

    default:
      return DELETE_PLAN_ERR_UNKNOWN;
  }
}
