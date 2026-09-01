import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { RuleFormComponent } from './rule-form.component';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { Plan } from '../models/plan.model';
import { Rule } from '../models/rule.model';

const PLAN_ID = 'plan-1';
const RULE_ID = '8f1c2d3e-4a5b-4c6d-8e9f-0a1b2c3d4e5f';

function makeRule(overrides: Partial<Rule> = {}): Rule {
  return {
    id: RULE_ID,
    name: 'Rule Test #1',
    sortOrder: 1,
    isActive: true,
    trigger: null,
    modifier: null,
    cap: null,
    floor: null,
    measurement: {
      _schema: 1 as const,
      type: 'Revenue',
      sourceField: 'amount',
      aggregation: 'Sum',
    },
    rateTable: { _schema: 1 as const, type: 'Flat', flatRate: 0.05 },
    ...overrides,
  } as unknown as Rule;
}

function makePlan(rules: Rule[]): Plan {
  return {
    id: PLAN_ID,
    name: 'Plan',
    status: 'Draft',
    currency: 'EUR',
    version: 1,
    rules,
  } as unknown as Plan;
}

describe('Rule form — coded save errors and the missing rule', () => {
  let httpMock: HttpTestingController;
  let toast: jasmine.SpyObj<ToastService>;
  let store: { selectedPlan: unknown; loadPlan: jasmine.Spy; updateRule: jasmine.Spy; addRule: jasmine.Spy };

  function setup(plan: Plan, ruleId: string | null): RuleFormComponent {
    const planSignal = signal<Plan | null>(plan);
    toast = jasmine.createSpyObj('ToastService', ['show']);
    store = {
      selectedPlan: planSignal,
      loadPlan: jasmine.createSpy('loadPlan').and.returnValue(Promise.resolve()),
      updateRule: jasmine.createSpy('updateRule').and.returnValue(Promise.resolve()),
      addRule: jasmine.createSpy('addRule').and.returnValue(Promise.resolve()),
    };

    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PlansStore, useValue: store as unknown as PlansStore },
        { provide: ToastService, useValue: toast },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (k: string) => (k === 'planId' ? PLAN_ID : k === 'ruleId' ? ruleId : null) },
            },
          },
        },
      ],
    });
    TestBed.overrideComponent(RuleFormComponent, {
      set: { imports: [ReactiveFormsModule, TranslateModule], template: `<form [formGroup]="form"></form>` },
    });
    httpMock = TestBed.inject(HttpTestingController);
    return TestBed.createComponent(RuleFormComponent).componentInstance;
  }

  function flushCatalogs(): void {
    httpMock.expectOne('/api/plans/trigger-fields').flush([]);
    httpMock.expectOne('/api/plans/category-values').flush([]);
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  // ── The rule the URL names does not exist ─────────────────────────────────────────────────

  /**
   * ★★ THE BLANK FORM. `find(r => r.id === ruleId)` followed by a bare `return` left an untouched,
   * fully enabled Add-a-rule form under an "Edit rule" heading, with no error anywhere on screen.
   */
  it('reports a missing rule instead of rendering an empty form', fakeAsync(() => {
    const component = setup(makePlan([makeRule()]), 'a-rule-that-was-deleted');
    component.ngOnInit();
    flushCatalogs();
    tick();

    expect(component.ruleNotFound()).toBe(true);
    expect(component.form.disabled).toBe(true);
  }));

  /**
   * ★ AND IT WILL NOT SAVE. The blank form was not merely uninformative: submitting it would have
   * created a SECOND rule from a form the user believed they were editing.
   */
  it('refuses to submit while the rule is missing', fakeAsync(() => {
    const component = setup(makePlan([makeRule()]), 'a-rule-that-was-deleted');
    component.ngOnInit();
    flushCatalogs();
    tick();

    void component.onSubmit();
    tick();

    expect(store.addRule).not.toHaveBeenCalled();
    expect(store.updateRule).not.toHaveBeenCalled();
  }));

  /**
   * ★ THE CASE-SENSITIVITY HALF. The API emits GUIDs lower-cased, so a link carrying the id in upper
   * case — pasted out of the database, a log line or an email — matched nothing and produced exactly
   * the blank form above.
   */
  it('finds the rule when the URL spells its id in upper case', fakeAsync(() => {
    const component = setup(makePlan([makeRule()]), RULE_ID.toUpperCase());
    component.ngOnInit();
    flushCatalogs();
    tick();

    expect(component.ruleNotFound()).toBe(false);
    expect(component.form.controls.name.value).toBe('Rule Test #1');
  }));

  /** A plan that never loaded is a different failure and must not be described as a missing rule. */
  it('does not claim the rule is missing when the plan itself did not load', fakeAsync(() => {
    const component = setup(null as unknown as Plan, RULE_ID);
    component.ngOnInit();
    flushCatalogs();
    tick();

    expect(component.ruleNotFound()).toBe(false);
  }));

  // ── The coded refusals reaching the toast ─────────────────────────────────────────────────

  function submitFailingWith(error: unknown): RuleFormComponent {
    const component = setup(makePlan([makeRule()]), RULE_ID);
    component.ngOnInit();
    flushCatalogs();
    tick();

    store.updateRule.and.returnValue(Promise.reject(error));
    void component.onSubmit();
    tick();

    return component;
  }

  /**
   * ★★ THE POINT OF THE WHOLE PASS. The toast gets a KEY and the values to fill it, so the sentence
   * is written in the reader's language — not an English one assembled in C#.
   */
  it('shows a coded ladder refusal as a translated key with its values', fakeAsync(() => {
    submitFailingWith(new HttpErrorResponse({
      status: 422,
      error: {
        status: 422,
        code: 'RateTableTiersOverlap',
        parameters: { tierNumber: 1, nextTierNumber: 2, endsAt: 100, nextStartsAt: 80 },
      },
    }));

    expect(toast.show).toHaveBeenCalledWith(
      'PLANS.RATE_TABLE_ERR_OVERLAP',
      'error',
      { tierNumber: 1, nextTierNumber: 2, endsAt: 100, nextStartsAt: 80 },
    );
  }));

  it('picks the attainment wording from the bound the server sent', fakeAsync(() => {
    submitFailingWith(new HttpErrorResponse({
      status: 422,
      error: {
        status: 422,
        code: 'RateTableLastTierMustBeOpen',
        parameters: { tierNumber: 2, endsAt: 7500, bound: 'AttainmentRatio' },
      },
    }));

    expect(toast.show).toHaveBeenCalledWith(
      'PLANS.RATE_TABLE_ERR_LAST_BOUNDED_RATIO',
      'error',
      { tierNumber: 2, endsAt: 7500 },
    );
  }));

  /**
   * ★ A CODED ERROR THIS BUILD DOES NOT RECOGNISE IS NOT ASSUMED TO BE A LADDER PROBLEM. It falls
   * through to the plain message path rather than being described as a bad rate table — and in no
   * case does the raw code reach the screen.
   */
  it('falls back to the plain message for a code it does not know', fakeAsync(() => {
    submitFailingWith(new HttpErrorResponse({
      status: 409,
      error: { code: 'AccountSnapshotStale', message: 'Something else entirely.' },
    }));

    const [key] = toast.show.calls.mostRecent().args;
    expect(key).toBe('Something else entirely.');
    expect(key).not.toContain('AccountSnapshotStale');
  }));

  it('still shows the old message-only error shape unchanged', fakeAsync(() => {
    submitFailingWith(new HttpErrorResponse({
      status: 422,
      error: { message: 'Only Per Transaction cap scope is currently supported.' },
    }));

    expect(toast.show).toHaveBeenCalledWith(
      'Only Per Transaction cap scope is currently supported.',
      'error',
    );
  }));
});
