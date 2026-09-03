import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { PlanDetailComponent } from './plan-detail.component';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { SubscriptionStateService } from '../../subscription/services/subscription-state.service';
import { Plan, PlanStatus } from '../models/plan.model';
import { Rule, MeasurementType, MeasurementAggregation, RateTableType } from '../models/rule.model';

/**
 * A STOPPED RULE HAS TO BE ON SCREEN — on BOTH screens that list rules.
 *
 * ★★ WHY THIS SPEC EXISTS, AND WHY THE EXISTING TESTS DID NOT CATCH IT. The backend fix landed and
 * was tested (`GetPlanByIdHandler` returns stopped rules), the domain fix landed and was tested (the
 * clone carries them), and the DOM still showed nothing: `sortedRules()` filtered `isActive` and
 * threw the rule away after all of that. Every test passed through a path production does not take —
 * none of them rendered the list. This one looks at the real template (§A3).
 *
 * ★ AND IT ASSERTS BOTH SCREENS. The Active plan and its cloned Draft are the SAME component on the
 * same route (`/plans/:planId`), so one filter hid the rule twice. A test covering one of them would
 * have gone green on a half-fix.
 */
describe('PlanDetailComponent — the rules list shows stopped rules', () => {
  const PLAN_ID = 'plan-1';

  function makeRule(overrides: Partial<Rule> = {}): Rule {
    return {
      id: 'rule-1',
      name: 'Solo LAP',
      sortOrder: 1,
      isActive: true,
      trigger: null,
      modifier: null,
      cap: null,
      floor: null,
      measurement: {
        type: MeasurementType.Revenue,
        sourceField: 'amount',
        aggregation: MeasurementAggregation.Sum,
      },
      rateTable: { type: RateTableType.Flat, flatRate: 0.05 },
      stoppedAt: null,
      stoppedBy: null,
      stopReason: null,
      ...overrides,
    } as unknown as Rule;
  }

  /** A rule braked on a live plan: inactive AND marked. */
  function stoppedRule(overrides: Partial<Rule> = {}): Rule {
    return makeRule({
      isActive: false,
      stoppedAt: '2026-09-01T08:30:00Z',
      stoppedBy: 'comp-manager-1',
      stopReason: 'The rate was written incorrectly',
      ...overrides,
    });
  }

  /** A rule removed from a draft: inactive and NOT marked. The distinction that must survive. */
  function removedRule(overrides: Partial<Rule> = {}): Rule {
    return makeRule({ id: 'rule-removed', name: 'Never shipped', isActive: false, ...overrides });
  }

  function makePlan(status: PlanStatus, rules: Rule[]): Plan {
    return {
      id: PLAN_ID,
      tenantId: 'tenant-1',
      name: 'Test SKU Laptops',
      description: '',
      version: 1,
      status,
      effectiveStart: '2026-01-01',
      effectiveEnd: '2026-12-31',
      currency: 'EUR',
      createdAt: '2026-01-01T00:00:00Z',
      createdBy: 'user-1',
      rules,
      activeAssignmentCount: 0,
      clawbackMaturationDays: null,
      clawbackCapPercent: null,
    } as unknown as Plan;
  }

  function makeFixture(plan: Plan) {
    TestBed.configureTestingModule({
      imports: [PlanDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          // The whole store surface the component and its template touch. Anything missing throws
          // inside change detection, which reads as a failing assertion about the rules list.
          provide: PlansStore,
          useValue: {
            selectedPlan: signal<Plan | null>(plan) as unknown as PlansStore['selectedPlan'],
            versions: signal([]) as unknown as PlansStore['versions'],
            unfilteredTotal: signal(0) as unknown as PlansStore['unfilteredTotal'],
            loading: signal(false) as unknown as PlansStore['loading'],
            error: signal(null) as unknown as PlansStore['error'],
            loadPlan: jasmine.createSpy('loadPlan').and.returnValue(Promise.resolve()),
            loadVersions: jasmine.createSpy('loadVersions').and.returnValue(Promise.resolve()),
            activatePlan: jasmine.createSpy('activatePlan').and.returnValue(Promise.resolve()),
            archivePlan: jasmine.createSpy('archivePlan').and.returnValue(Promise.resolve()),
            clonePlan: jasmine.createSpy('clonePlan').and.returnValue(Promise.resolve(plan)),
            deleteRule: jasmine.createSpy('deleteRule').and.returnValue(Promise.resolve()),
          },
        },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        {
          // AppShell wraps this page and calls load() on init, so the stub needs the whole surface
          // the shell touches — not just the signal this component reads.
          provide: SubscriptionStateService,
          useValue: {
            subscription: signal(null),
            loaded: signal(true),
            isPastDue: signal(false),
            isCanceled: signal(false),
            load: () => {},
            refresh: () => {},
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (k: string) => (k === 'planId' ? PLAN_ID : null) },
              queryParams: {},
            },
            // A real Observable: ngOnInit pipes this through bindFiltersToUrl, and a hand-rolled
            // stub with only `subscribe` throws before a single rule is ever rendered.
            queryParams: of({}),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PlanDetailComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  function ruleNames(fixture: ReturnType<typeof makeFixture>): string[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('.rule-card__name') as NodeListOf<HTMLElement>,
    ).map((el) => el.textContent!.trim());
  }

  // ── Screen 1: the Active plan whose only rule was braked ──────────────────

  it('★ Active plan: the stopped rule is on screen, not an empty list', () => {
    const fixture = makeFixture(makePlan('Active', [stoppedRule()]));

    expect(fixture.componentInstance.sortedRules().length)
      .withContext('the header said "Rules 1" while the list rendered nothing')
      .toBe(1);
    expect(ruleNames(fixture)).toEqual(['Solo LAP']);
  });

  it('Active plan: it carries its badge, its date and its reason', () => {
    const fixture = makeFixture(makePlan('Active', [stoppedRule()]));
    const card: HTMLElement = fixture.nativeElement.querySelector('.rule-card');

    expect(card.classList).withContext('a stopped rule is styled as stopped').toContain('rule-card--stopped');
    expect(card.querySelector('.rule-card__stopped')).withContext('the marker block is rendered').toBeTruthy();
    expect(card.querySelector('.rule-card__stopped-why')!.textContent)
      .toContain('The rate was written incorrectly');
    // Not dimmed: --inactive is for a rule removed from a draft, and a stopped one has to be read.
    expect(card.classList).not.toContain('rule-card--inactive');
  });

  it('Active plan: the derived "no rule in effect" warning still shows alongside it', () => {
    const fixture = makeFixture(makePlan('Active', [stoppedRule()]));

    expect(fixture.componentInstance.hasNoLiveRules())
      .withContext('showing the rule must not silence the warning — both are true at once')
      .toBeTrue();
    expect(fixture.nativeElement.querySelector('.plan-no-live-rules')).toBeTruthy();
  });

  it('Active plan: no Stop button on a rule already stopped — there is no second stop', () => {
    const fixture = makeFixture(makePlan('Active', [stoppedRule()]));
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('.rule-card__actions ws-button') as NodeListOf<HTMLElement>,
    );
    expect(buttons.some((b) => b.textContent!.includes('STOP_RULE'))).toBeFalse();
  });

  // ── Screen 2: the cloned Draft ────────────────────────────────────────────

  it('★ cloned Draft: the stopped rule is on screen, not an empty list', () => {
    const fixture = makeFixture(makePlan('Draft', [stoppedRule({ id: 'clone-rule-1' })]));

    expect(fixture.componentInstance.sortedRules().length).toBe(1);
    expect(ruleNames(fixture)).toEqual(['Solo LAP']);
    expect(fixture.nativeElement.querySelector('.rule-card--stopped')).toBeTruthy();
  });

  it('cloned Draft: the empty state is NOT shown when the only rule is stopped', () => {
    const fixture = makeFixture(makePlan('Draft', [stoppedRule({ id: 'clone-rule-1' })]));
    expect(fixture.nativeElement.querySelector('.rules-empty'))
      .withContext('an empty state over a plan that has a rule is the bug being fixed')
      .toBeFalsy();
  });

  // ── The distinction that must survive the fix ─────────────────────────────

  it('★ a rule REMOVED from a draft stays hidden — the fix must not become "show everything"', () => {
    const fixture = makeFixture(makePlan('Draft', [makeRule(), removedRule()]));

    expect(ruleNames(fixture))
      .withContext('a removed rule reappearing breaks its Edit action')
      .toEqual(['Solo LAP']);
  });

  it('stopped and removed are told apart in the same list', () => {
    const fixture = makeFixture(
      makePlan('Draft', [
        makeRule({ id: 'live', name: 'Live one', sortOrder: 1 }),
        stoppedRule({ id: 'stopped', name: 'Braked one', sortOrder: 2 }),
        removedRule({ sortOrder: 3 }),
      ]),
    );

    expect(ruleNames(fixture)).toEqual(['Live one', 'Braked one']);
    expect(fixture.nativeElement.querySelectorAll('.rule-card--stopped').length).toBe(1);
  });

  it('a plan whose rules are all live is unchanged by the fix', () => {
    const fixture = makeFixture(makePlan('Active', [makeRule()]));

    expect(ruleNames(fixture)).toEqual(['Solo LAP']);
    expect(fixture.nativeElement.querySelector('.rule-card--stopped')).toBeFalsy();
    expect(fixture.componentInstance.hasNoLiveRules()).toBeFalse();
  });

  // ── Alignment, MEASURED ───────────────────────────────────────────────────

  /**
   * ★★ A REAL MEASUREMENT, NOT A CLASS-NAME ASSERTION. Karma runs a real Chrome, so this test lays
   * the card out and reads where things actually are. That matters here because the whole bug class
   * is invisible to the checks above: every element was present, with the right classes, on the
   * wrong x. §A2's warning is about jsdom having no layout — this suite does, so it should use it.
   *
   * The card has TWO columns and must have exactly two: the marker ("#N", or the warning icon) and
   * everything the card says. The first attempt at this aligned the stop BLOCK to the gutter, which
   * put the icon on the name's line and shoved the text one icon-width further right — a third left
   * edge that reads as "almost aligned", the kind nobody files a bug about.
   */
  function leftOf(fixture: ReturnType<typeof makeFixture>, selector: string): number {
    const el: HTMLElement = fixture.nativeElement.querySelector(selector);
    expect(el).withContext(`${selector} is on screen`).toBeTruthy();
    return el.getBoundingClientRect().left;
  }

  it('★ the name, the tags and the stop reason share one left edge', () => {
    const fixture = makeFixture(makePlan('Active', [stoppedRule()]));

    const name = leftOf(fixture, '.rule-card__name');
    const tags = leftOf(fixture, '.rule-card__tags');
    const when = leftOf(fixture, '.rule-card__stopped-when');
    const why = leftOf(fixture, '.rule-card__stopped-why');

    // Sub-pixel tolerance only: these are supposed to be the same line, not merely close.
    expect(Math.abs(tags - name)).withContext('tags vs name').toBeLessThan(1);
    expect(Math.abs(when - name)).withContext('stop date vs name').toBeLessThan(1);
    expect(Math.abs(why - name)).withContext('stop reason vs name').toBeLessThan(1);
  });

  it('the warning icon sits in the marker column, under the "#N"', () => {
    const fixture = makeFixture(makePlan('Active', [stoppedRule()]));

    const order = leftOf(fixture, '.rule-card__order');
    const icon = leftOf(fixture, '.rule-card__stopped app-icon');

    expect(Math.abs(icon - order))
      .withContext('the icon is a marker, so it belongs on the marker line')
      .toBeLessThan(1);
  });

  /**
   * The alignment has to hold BETWEEN cards, which is why the marker column is reserved rather than
   * measured: "#1" is narrower than "#12", so a gutter that merely cleared the number would put each
   * card's name on a slightly different x.
   */
  it('★ cards with different order numbers still line up with each other', () => {
    const fixture = makeFixture(
      makePlan('Active', [
        makeRule({ id: 'a', name: 'First', sortOrder: 1 }),
        makeRule({ id: 'b', name: 'Tenth', sortOrder: 10 }),
        makeRule({ id: 'c', name: 'Hundredth', sortOrder: 100 }),
      ]),
    );

    const names = Array.from(
      fixture.nativeElement.querySelectorAll('.rule-card__name') as NodeListOf<HTMLElement>,
    ).map((el) => el.getBoundingClientRect().left);

    expect(names.length).toBe(3);
    for (const left of names) {
      expect(Math.abs(left - names[0]))
        .withContext('a one-digit and a three-digit order must not shift the name')
        .toBeLessThan(1);
    }
  });

  // ── The symptom itself ────────────────────────────────────────────────────

  /**
   * The tab count reads the RAW payload while the list read the filtered one, so the two disagreeing
   * was the visible face of this bug: "Rules 1" over an empty list. They have to agree whenever the
   * only difference is a stopped rule.
   */
  it('★ the tab count and the list agree', () => {
    const plan = makePlan('Active', [stoppedRule()]);
    const fixture = makeFixture(plan);

    expect(fixture.componentInstance.sortedRules().length).toBe(plan.rules.length);
  });
});
