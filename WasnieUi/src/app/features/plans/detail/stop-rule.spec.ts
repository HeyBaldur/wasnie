import { HttpErrorResponse } from '@angular/common/http';
import { extractApiErrorCode } from '../../../shared/utils/api-error';
import { stopRuleErrorKey, stopRuleErrorParams, STOP_RULE_ERR_UNKNOWN } from './stop-rule-error';
import { getPlanPermissions } from '../services/plan-permissions';
import { isRuleStopped } from '../models/rule.model';
import type { Rule } from '../models/rule.model';
import { MeasurementType, MeasurementAggregation, RateTableType } from '../models/rule.model';
import en from '../../../../assets/i18n/en.json';
import es from '../../../../assets/i18n/es.json';
import pl from '../../../../assets/i18n/pl.json';

/**
 * THE EMERGENCY BRAKE, front-end half (KAN-29).
 *
 * ★ WHAT IS ACTUALLY BEING GUARDED. Not that the button posts — that is one line. It is that the UI
 * keeps telling apart the two meanings of `isActive: false`: a rule REMOVED from a draft, and a rule
 * STOPPED on a live plan. Collapse them and a braked plan looks like it never had that rule, which
 * is the silence this feature was built to end.
 */

function rule(overrides: Partial<Rule> = {}): Rule {
  return {
    id: 'r1',
    name: 'Base commission',
    sortOrder: 1,
    isActive: true,
    trigger: null,
    measurement: {
      type: MeasurementType.Revenue,
      sourceField: 'amount',
      aggregation: MeasurementAggregation.Sum,
    } as Rule['measurement'],
    rateTable: { type: RateTableType.Flat } as Rule['rateTable'],
    modifier: null,
    cap: null,
    floor: null,
    stoppedAt: null,
    stoppedBy: null,
    stopReason: null,
    ...overrides,
  };
}

describe('stopped vs removed', () => {
  it('a live rule is neither', () => {
    expect(isRuleStopped(rule())).toBe(false);
  });

  it('a rule removed from a draft is inactive but NOT stopped', () => {
    expect(isRuleStopped(rule({ isActive: false }))).toBe(false);
  });

  it('a rule braked on a live plan is stopped', () => {
    const stopped = rule({ isActive: false, stoppedAt: '2026-09-01T10:00:00Z', stopReason: 'rate typo' });
    expect(isRuleStopped(stopped)).toBe(true);
  });
});

describe('who may pull the brake', () => {
  it('is offered on an Active plan and nowhere else', () => {
    // The one rule action that exists BECAUSE the plan is live — the opposite of every other flag.
    expect(getPlanPermissions('Active').canStopRule).toBe(true);
    expect(getPlanPermissions('Draft').canStopRule).toBe(false);
    expect(getPlanPermissions('Archived').canStopRule).toBe(false);
    expect(getPlanPermissions(null).canStopRule).toBe(false);
  });

  it('a Draft still edits and removes rules outright — there is nothing to brake', () => {
    const draft = getPlanPermissions('Draft');
    expect(draft.canEditRule).toBe(true);
    expect(draft.canDeleteRule).toBe(true);
  });
});

describe('coded refusals', () => {
  function coded(code: string, parameters: Record<string, unknown> = {}) {
    return extractApiErrorCode(
      new HttpErrorResponse({ status: 422, error: { status: 422, code, parameters } }),
    )!;
  }

  it('maps every code the backend can emit to a literal key', () => {
    expect(stopRuleErrorKey(coded('RuleAlreadyStopped'))).toBe('PLANS.STOP_RULE_ERR_ALREADY_STOPPED');
    expect(stopRuleErrorKey(coded('RuleStopReasonRequired'))).toBe('PLANS.STOP_RULE_ERR_REASON_REQUIRED');
    expect(stopRuleErrorKey(coded('RuleStopReasonTooLong'))).toBe('PLANS.STOP_RULE_ERR_REASON_TOO_LONG');
    expect(stopRuleErrorKey(coded('RuleStopPlanNotActive'))).toBe('PLANS.STOP_RULE_ERR_PLAN_NOT_ACTIVE');
    expect(stopRuleErrorKey(coded('RuleStopRuleNotFound'))).toBe('PLANS.STOP_RULE_ERR_RULE_NOT_FOUND');
  });

  /**
   * ★ THE POINT OF THE WHITELIST. A key built as `PLANS.STOP_RULE_ERR_${code}` would print the raw
   * internal identifier the first time the backend adds an invariant this build has no wording for.
   */
  it('falls back to a generic line for a code it does not know, never to the code itself', () => {
    const key = stopRuleErrorKey(coded('RuleStopSomethingNewNobodyTranslated'));
    expect(key).toBe(STOP_RULE_ERR_UNKNOWN);
    expect(key).not.toContain('RuleStopSomethingNewNobodyTranslated');
  });

  it('passes the numbers through unformatted, for a sentence to interpolate', () => {
    expect(stopRuleErrorParams(coded('RuleStopReasonTooLong', { maxLength: 500 }))).toEqual({ maxLength: 500 });
  });
});

describe('i18n completeness', () => {
  const KEYS = [
    'RULE_STOPPED',
    'RULE_STOPPED_ON',
    'ACTION_STOP_RULE',
    'TOAST_RULE_STOPPED',
    'NO_LIVE_RULES_TITLE',
    'NO_LIVE_RULES_BODY',
    'STOP_RULE_TITLE',
    'STOP_RULE_DESC',
    'STOP_RULE_LIVE_CREDITS',
    'STOP_RULE_LIVE_CREDITS_UNKNOWN',
    'STOP_RULE_LAST_WARNING',
    'STOP_RULE_IRREVERSIBLE',
    'STOP_RULE_REASON_LABEL',
    'STOP_RULE_REASON_PLACEHOLDER',
    'STOP_RULE_ERR_ALREADY_STOPPED',
    'STOP_RULE_ERR_REASON_REQUIRED',
    'STOP_RULE_ERR_REASON_TOO_LONG',
    'STOP_RULE_ERR_PLAN_NOT_ACTIVE',
    'STOP_RULE_ERR_RULE_NOT_FOUND',
    'STOP_RULE_ERR_UNKNOWN',
  ];

  const DASHBOARD_KEYS = [
    'NO_LIVE_RULES_GROUP_TITLE',
    'NO_LIVE_RULES_EXPLAIN',
    'NO_LIVE_RULES_SINCE',
    'NO_LIVE_RULES_ASSIGNED',
    'NO_LIVE_RULES_ASSIGNED_NONE',
  ];

  const files: Array<[string, Record<string, Record<string, string>>]> = [
    ['en', en as never],
    ['es', es as never],
    ['pl', pl as never],
  ];

  for (const [lang, dict] of files) {
    it(`${lang} has every plans key, non-empty`, () => {
      for (const key of KEYS) {
        expect(`${key}=${dict['PLANS'][key] ?? ''}`).not.toBe(`${key}=`);
      }
    });

    it(`${lang} has every dashboard key, non-empty`, () => {
      for (const key of DASHBOARD_KEYS) {
        expect(`${key}=${dict['DASHBOARD'][key] ?? ''}`).not.toBe(`${key}=`);
      }
    });
  }

  /**
   * ★ NOT MERELY PRESENT — ACTUALLY TRANSLATED. A Spanish file that echoes the English string is the
   * failure this catches: the key exists, the test that only checks presence passes, and the reader
   * still gets English. Checked on the longest strings, where a real translation cannot coincide.
   */
  it('es and pl are not English copies', () => {
    for (const key of ['STOP_RULE_LAST_WARNING', 'STOP_RULE_IRREVERSIBLE', 'NO_LIVE_RULES_BODY']) {
      expect((es as never as Record<string, Record<string, string>>)['PLANS'][key])
        .not.toBe((en as never as Record<string, Record<string, string>>)['PLANS'][key]);
      expect((pl as never as Record<string, Record<string, string>>)['PLANS'][key])
        .not.toBe((en as never as Record<string, Record<string, string>>)['PLANS'][key]);
    }
  });

  /** The reason is the record; a label that does not say it is required invites an empty box. */
  it('the reason label is marked required in all three languages', () => {
    expect((en as never as Record<string, Record<string, string>>)['PLANS']['STOP_RULE_REASON_LABEL']).toContain('required');
    expect((es as never as Record<string, Record<string, string>>)['PLANS']['STOP_RULE_REASON_LABEL']).toContain('obligatorio');
    expect((pl as never as Record<string, Record<string, string>>)['PLANS']['STOP_RULE_REASON_LABEL']).toContain('wymagany');
  });
});
