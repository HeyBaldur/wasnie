import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { PayRunsListComponent } from './pay-runs-list.component';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunsStore } from '../state/pay-runs.store';
import { PlansApiService } from '../../plans/services/plans.api.service';
import en from '../../../../assets/i18n/en.json';
import es from '../../../../assets/i18n/es.json';
import pl from '../../../../assets/i18n/pl.json';

const makeStore = () => ({
  items: signal([]),
  total: signal(0),
  totalCount: signal(0),
  loading: signal(false),
  page: signal(1),
  pageSize: signal(25),
  totalPages: signal(1),
  filter: signal({}),
  setFilter: jasmine.createSpy('setFilter'),
  setPage: jasmine.createSpy('setPage'),
  reload: jasmine.createSpy('reload').and.returnValue(Promise.resolve()),
  toExportParams: jasmine.createSpy('toExportParams').and.returnValue({}),
});

describe('PayRunsListComponent — plan selector', () => {
  let component: PayRunsListComponent;

  const plansApiSpy = jasmine.createSpyObj<PlansApiService>('PlansApiService', ['getPlans']);

  beforeEach(async () => {
    plansApiSpy.getPlans.and.returnValue(of({
      items: [
        { id: 'plan-1', name: 'Q1 2026', effectiveStart: '2026-01-01', effectiveEnd: '2026-03-31', status: 'Active', currency: 'EUR' },
        { id: 'plan-2', name: 'Q2 2026', effectiveStart: '2026-04-01', effectiveEnd: '2026-06-30', status: 'Active', currency: 'EUR' },
      ],
      totalCount: 2, page: 1, pageSize: 20, totalPages: 1, hasNextPage: false, hasPreviousPage: false,
    } as any));

    await TestBed.configureTestingModule({
      imports: [PayRunsListComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: PayRunsApiService, useValue: jasmine.createSpyObj('PayRunsApiService', ['listPayRuns', 'calculate', 'exportPayRuns', 'deleteDraft']) },
        { provide: PayRunsStore, useValue: makeStore() },
        { provide: PlansApiService, useValue: plansApiSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PayRunsListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('planSearchFn maps plan items to SelectOptions with date range in label', fakeAsync(() => {
    let result: { value: string; label: string }[] = [];
    component.planSearchFn('Q1').subscribe(opts => (result = opts as typeof result));
    tick();

    expect(result.length).toBe(2);
    expect(result[0].value).toBe('plan-1');
    expect(result[0].label).toContain('Q1 2026');
    expect(result[0].label).toContain('2026-01-01');
    expect(result[0].label).toContain('2026-03-31');
  }));

  it('selecting a plan auto-fills periodStart and periodEnd', fakeAsync(() => {
    // Trigger searchFn to populate the internal date cache
    component.planSearchFn('').subscribe();
    tick();

    component.calculateForm.controls.planId.setValue('plan-1');
    tick();

    expect(component.calculateForm.value.periodStart).toBe('2026-01-01');
    expect(component.calculateForm.value.periodEnd).toBe('2026-03-31');
  }));

  it('selecting a different plan overwrites the previously auto-filled dates', fakeAsync(() => {
    component.planSearchFn('').subscribe();
    tick();

    component.calculateForm.controls.planId.setValue('plan-1');
    tick();
    component.calculateForm.controls.planId.setValue('plan-2');
    tick();

    expect(component.calculateForm.value.periodStart).toBe('2026-04-01');
    expect(component.calculateForm.value.periodEnd).toBe('2026-06-30');
  }));

  it('closeCalculateModal resets the form including planId', fakeAsync(() => {
    component.planSearchFn('').subscribe();
    tick();
    component.calculateForm.controls.planId.setValue('plan-1');
    tick();

    component.closeCalculateModal();

    expect(component.calculateForm.value.planId).toBeNull();
    expect(component.calculateForm.value.periodStart).toBeNull();
    expect(component.calculateForm.value.periodEnd).toBeNull();
  }));
});


// ══ Why nothing was created ══════════════════════════════════════════════════
//
// ★★ THE DEFECT THESE PIN. The screen turned `payoutsCreated === 0` into "No payouts created. No
// matching credits found for this period." — a cause the backend never established. In the run that
// prompted this it was false twice: four assignments were dropped because their payee had left, all
// twenty survivors hit an already-Paid payout, and no credit was ever queried. Every assertion below
// is about the screen refusing to say more than the engine reported.

describe('PayRunsListComponent — explaining a run that created nothing', () => {
  let component: PayRunsListComponent;

  const result = (over: any = {}): any => ({
    payRunId: 'run-1',
    payoutsCreated: 0,
    conflicts: [],
    warnings: [],
    isSupplemental: false,
    supplementalSequence: 0,
    diagnostics: {
      assignmentsConsidered: 0,
      assignmentsReachingCreditLookup: 0,
      creditsExamined: 0,
      skipped: [],
      ...(over.diagnostics ?? {}),
    },
    ...over,
  });

  beforeEach(async () => {
    const plansApiSpy = jasmine.createSpyObj<PlansApiService>('PlansApiService', ['getPlans']);
    plansApiSpy.getPlans.and.returnValue(of({
      items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 1,
      hasNextPage: false, hasPreviousPage: false,
    } as any));

    await TestBed.configureTestingModule({
      imports: [PayRunsListComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: PayRunsApiService, useValue: jasmine.createSpyObj('PayRunsApiService', ['listPayRuns', 'calculate', 'exportPayRuns', 'deleteDraft']) },
        { provide: PayRunsStore, useValue: makeStore() },
        { provide: PlansApiService, useValue: plansApiSpy },
      ],
    }).compileComponents();

    component = TestBed.createComponent(PayRunsListComponent).componentInstance;
  });

  // ── The headline ───────────────────────────────────────────────────────────

  it('says there was nothing to calculate when no assignment covered the period', () => {
    const r = result({ diagnostics: { assignmentsConsidered: 0 } });
    expect(component.noPayoutsHeadlineKey(r)).toBe('PAY_RUNS.CALCULATE_NOTHING_TO_CONSIDER');
  });

  it('says assignments were considered and skipped when that is what happened', () => {
    const r = result({
      diagnostics: {
        assignmentsConsidered: 24,
        skipped: [{ code: 'TerminatedPayee', count: 4 }, { code: 'ExistingPayout', count: 20 }],
      },
    });
    expect(component.noPayoutsHeadlineKey(r)).toBe('PAY_RUNS.CALCULATE_ALL_SKIPPED');
  });

  /**
   * ★ THE RULE THIS WHOLE CHANGE EXISTS FOR. Assignments were considered, none produced a payout, and
   * the engine reported no reason. The screen must say the flat fact and claim NOTHING about why —
   * not credits, not dates.
   */
  it('claims no cause at all when the engine reported none', () => {
    const r = result({ diagnostics: { assignmentsConsidered: 3, skipped: [] } });
    expect(component.noPayoutsHeadlineKey(r)).toBe('PAY_RUNS.CALCULATE_NO_PAYOUTS_NEUTRAL');
  });

  it('falls back to the neutral headline if an older backend sends no diagnostics at all', () => {
    const r = result();
    r.diagnostics = undefined;
    expect(component.noPayoutsHeadlineKey(r)).toBe('PAY_RUNS.CALCULATE_NO_PAYOUTS_NEUTRAL');
    expect(component.skipCounts(r)).toEqual([]);
    expect(component.neverLookedAtCredits(r)).toBeFalse();
    expect(component.terminatedSkipCount(r)).toBe(0);
  });

  // ── The reason codes ───────────────────────────────────────────────────────

  it('maps every reason code the engine can emit', () => {
    expect(component.skipLabelKey('TerminatedPayee')).toBe('PAY_RUNS.SKIP_TerminatedPayee');
    expect(component.skipLabelKey('PlanNotPayable')).toBe('PAY_RUNS.SKIP_PlanNotPayable');
    expect(component.skipLabelKey('ExistingPayout')).toBe('PAY_RUNS.SKIP_ExistingPayout');
  });

  /**
   * ★ AN UNKNOWN CODE DEGRADES, IT DOES NOT GUESS. Concatenating `PAY_RUNS.SKIP_${code}` blindly would
   * print a raw backend identifier the first time a new code ships ahead of its translation. The
   * fallback still reports that something was skipped and stays silent about why.
   */
  it('degrades an unrecognised code to a neutral label instead of inventing a cause', () => {
    expect(component.skipLabelKey('SomethingNewInTheEngine')).toBe('PAY_RUNS.SKIP_UNKNOWN');
    expect(component.skipLabelKey('')).toBe('PAY_RUNS.SKIP_UNKNOWN');
  });

  // ── The fact that killed the old sentence ──────────────────────────────────

  it('reports that credits were never looked at when no assignment reached the lookup', () => {
    const r = result({
      diagnostics: {
        assignmentsConsidered: 24,
        assignmentsReachingCreditLookup: 0,
        skipped: [{ code: 'ExistingPayout', count: 24 }],
      },
    });
    expect(component.neverLookedAtCredits(r)).toBeTrue();
  });

  it('does not mention the credit lookup on a run that reached it', () => {
    const r = result({
      diagnostics: { assignmentsConsidered: 2, assignmentsReachingCreditLookup: 2, creditsExamined: 5 },
    });
    expect(component.neverLookedAtCredits(r)).toBeFalse();
  });

  it('does not mention the credit lookup when there was nothing to consider', () => {
    // An empty run never had a reason to enter that stage; saying so would be noise, not information.
    expect(component.neverLookedAtCredits(result())).toBeFalse();
  });

  // ── The link that closes the loop with the orphan queue ────────────────────

  it('surfaces the terminated count so the message can point at the orphan queue', () => {
    const r = result({
      diagnostics: {
        assignmentsConsidered: 24,
        skipped: [{ code: 'TerminatedPayee', count: 4 }, { code: 'ExistingPayout', count: 20 }],
      },
    });
    expect(component.terminatedSkipCount(r)).toBe(4);
  });

  it('does not point at the orphan queue when nobody was skipped for having left', () => {
    const r = result({
      diagnostics: { assignmentsConsidered: 3, skipped: [{ code: 'ExistingPayout', count: 3 }] },
    });
    expect(component.terminatedSkipCount(r)).toBe(0);
  });
});

// ══ The three languages ══════════════════════════════════════════════════════

describe('Pay-run skip reasons — EN / ES / PL', () => {
  // Imported directly rather than through the loader: this asserts the FILES are complete, which is
  // what "i18n is done" means. A missing key would otherwise silently fall back to the key name.
  const bundles: Record<string, any> = { en, es, pl };

  const keys = [
    'CALCULATE_NOTHING_TO_CONSIDER',
    'CALCULATE_ALL_SKIPPED',
    'CALCULATE_NO_PAYOUTS_NEUTRAL',
    'SKIPPED_TITLE',
    'SKIP_TerminatedPayee',
    'SKIP_PlanNotPayable',
    'SKIP_ExistingPayout',
    'SKIP_UNKNOWN',
    'NEVER_REACHED_CREDITS',
    'SKIPPED_TERMINATED_HINT',
    'SKIPPED_TERMINATED_LINK',
  ];

  for (const lang of ['en', 'es', 'pl']) {
    it(`has every skip-reason string in ${lang}`, () => {
      for (const key of keys) {
        expect(bundles[lang]['PAY_RUNS'][key])
          .withContext(`${lang}: PAY_RUNS.${key}`).toBeTruthy();
      }
    });
  }

  /**
   * ★ THE FALSE SENTENCE IS GONE FROM THE PAY-RUN SCREEN. It asserted a cause the backend never
   * established and cost an administrator three attempts at moving date ranges. If it comes back,
   * this fails.
   */
  it('no longer carries the sentence that invented a cause', () => {
    for (const lang of ['en', 'es', 'pl']) {
      expect(bundles[lang]['PAY_RUNS']['CALCULATE_NO_PAYOUTS'])
        .withContext(`${lang}: the retired key`).toBeUndefined();
    }
  });
});
