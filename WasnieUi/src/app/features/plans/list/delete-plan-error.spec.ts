import { HttpErrorResponse } from '@angular/common/http';
import { extractApiErrorCode } from '../../../shared/utils/api-error';
import { DELETE_PLAN_ERR_UNKNOWN, deletePlanErrorKey } from './delete-plan-error';
import en from '../../../../assets/i18n/en.json';
import es from '../../../../assets/i18n/es.json';
import pl from '../../../../assets/i18n/pl.json';

/**
 * KAN-69 — the refusal of `DELETE /api/plans/{id}` arrives as `422 { code, parameters }` with no message.
 * The fixtures are the exact bodies the endpoint returns (§A4: taken from the contract, pinned by the
 * integration test `DeletePlan_DraftAssignedThroughTheApi_Returns422WithCode_AndDeletesNothing`).
 */
describe('deletePlanErrorKey', () => {
  const refusal = (body: unknown) =>
    extractApiErrorCode(new HttpErrorResponse({ status: 422, error: body }))!;

  it('translates a Draft with dependencies', () => {
    const coded = refusal({ status: 422, code: 'PlanDeleteHasDependencies', parameters: { blockers: ['Assignments'] } });
    expect(deletePlanErrorKey(coded)).toBe('PLANS.DELETE_PLAN_ERR_HAS_DEPENDENCIES');
  });

  it('translates a plan that is no longer Draft', () => {
    const coded = refusal({ status: 422, code: 'PlanDeleteNotDraft', parameters: { status: 'Active' } });
    expect(deletePlanErrorKey(coded)).toBe('PLANS.DELETE_PLAN_ERR_NOT_DRAFT');
  });

  it('★ never prints an unknown code — it falls to the generic line (§C2)', () => {
    const coded = refusal({ status: 422, code: 'PlanDeleteSomethingNew', parameters: {} });
    expect(deletePlanErrorKey(coded)).toBe(DELETE_PLAN_ERR_UNKNOWN);
    expect(deletePlanErrorKey(coded)).not.toContain('SomethingNew');
  });

  it('every key it can return exists in EN, ES and PL', () => {
    const keys = ['DELETE_PLAN_ERR_HAS_DEPENDENCIES', 'DELETE_PLAN_ERR_NOT_DRAFT', 'DELETE_PLAN_ERR_UNKNOWN'];
    for (const dict of [en, es, pl] as unknown as Array<{ PLANS: Record<string, string> }>) {
      for (const key of keys) {
        expect(dict.PLANS[key]).withContext(key).toBeTruthy();
      }
    }
  });
});
