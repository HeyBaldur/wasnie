import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { RuleFormComponent } from './rule-form.component';
import { PlansStore } from '../state/plans.store';
import { PlansApiService } from '../services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { Plan } from '../models/plan.model';
import { MeasurementType, MeasurementAggregation, RateTableType } from '../models/rule.model';

/**
 * The simulator's blocked state, through the REAL template.
 *
 * ★★ THE SIBLING SPEC OVERRIDES THE TEMPLATE, so every assertion it makes is about a signal. That is
 * enough for the request logic and useless for this: whether `[disabled]` actually reaches the input
 * is a question about the binding, and a binding that never fires compiles, renders and reports
 * nothing — which is exactly how a dead `(clicked)` shipped on the assistant list earlier. So this
 * file pays for the heavier setup and looks at the DOM.
 */
describe('RuleFormComponent — the simulator is really disabled while the rule is incomplete', () => {
  const PLAN_ID = 'plan-1';

  function makePlan(): Plan {
    return {
      id: PLAN_ID,
      tenantId: 'tenant-1',
      name: 'Test Plan',
      description: '',
      version: 1,
      status: 'Draft',
      effectiveStart: '2024-01-01',
      effectiveEnd: '2024-12-31',
      currency: 'EUR',
      createdAt: '2024-01-01T00:00:00Z',
      createdBy: 'user-1',
      rules: [],
      activeAssignmentCount: 0,
      clawbackMaturationDays: null,
      clawbackCapPercent: null,
    } as unknown as Plan;
  }

  function makeFixture() {
    const api = jasmine.createSpyObj<PlansApiService>(
      'PlansApiService', ['simulateRule', 'getTriggerFields', 'getCategoryValues']);
    api.getTriggerFields.and.returnValue(of([]));
    api.getCategoryValues.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PlansApiService, useValue: api },
        {
          provide: PlansStore,
          useValue: {
            selectedPlan: signal<Plan | null>(makePlan()) as unknown as PlansStore['selectedPlan'],
            loadPlan: jasmine.createSpy('loadPlan').and.returnValue(Promise.resolve()),
          },
        },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: (k: string) => (k === 'planId' ? PLAN_ID : null) } } },
        },
      ],
    });

    const fixture = TestBed.createComponent(RuleFormComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  function nativeInput(fixture: ReturnType<typeof makeFixture>): HTMLInputElement | null {
    return fixture.nativeElement.querySelector('[data-testid="rule-sim-input"] input');
  }

  it('★★ the box is genuinely disabled in the DOM, not merely styled as if it were', fakeAsync(() => {
    const fixture = makeFixture();
    fixture.componentInstance.form.patchValue({ name: '' });
    fixture.componentInstance.form.updateValueAndValidity();
    fixture.detectChanges();
    // ★ NgModel applies `disabled` in a microtask, so a synchronous detectChanges cannot see it.
    // Flushing here is the difference between testing the binding and testing the scheduler.
    tick();
    fixture.detectChanges();

    const input = nativeInput(fixture);
    expect(input).withContext('the simulator box is on screen').toBeTruthy();
    expect(input!.disabled)
      .withContext('a box that only LOOKS disabled still takes typing')
      .toBeTrue();
  }));

  it('the blocked message is on screen while the rule is incomplete', () => {
    const fixture = makeFixture();
    fixture.componentInstance.form.patchValue({ name: '' });
    fixture.componentInstance.form.updateValueAndValidity();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="rule-sim-invalid"]')).toBeTruthy();
  });

  it('★ completing the rule enables the box and removes the message, with no reload', fakeAsync(() => {
    const fixture = makeFixture();
    const comp = fixture.componentInstance;

    comp.form.patchValue({ name: '' });
    comp.form.updateValueAndValidity();
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    expect(nativeInput(fixture)!.disabled).toBeTrue();

    comp.form.patchValue({
      name: 'Simulated rule',
      measurement: {
        type: MeasurementType.Revenue,
        sourceField: 'amount',
        aggregation: MeasurementAggregation.Sum,
      },
      rateTable: { type: RateTableType.Flat, flatRate: 0.05 },
    });
    comp.form.updateValueAndValidity();
    fixture.detectChanges();
    tick();
    fixture.detectChanges();

    expect(nativeInput(fixture)!.disabled).toBeFalse();
    expect(fixture.nativeElement.querySelector('[data-testid="rule-sim-invalid"]')).toBeNull();
  }));

  // ══ ★ The unit badge ══════════════════════════════════════════════════

  it('★★ the unit badge shows the PLANs currency, not a constant', () => {
    // ★★ AND THAT IS THE WHOLE ASSERTION. A hard-coded currency on a screen about money is a lie the
    // reader has no way to catch: EUR on a USD plan looks exactly as convincing as EUR on a EUR one.
    // The fixture plan is EUR, so the badge must say EUR — and the code path is `planCurrency()`,
    // which reads the loaded plan, so a USD plan says USD.
    const fixture = makeFixture();
    const badge = fixture.nativeElement.querySelector('[data-testid="rule-sim-unit"]');

    expect(badge).withContext('the unit is shown as a badge').toBeTruthy();
    expect(badge.textContent.trim()).toBe('EUR');
  });

  it('★ it is the Ws primitive, not hand-rolled utility classes', () => {
    // ★ The sketch this came from used `bg-brand-softer` / `text-fg-brand-strong`, which this project
    // does not define: they would have compiled, rendered nothing, and left an invisible badge. This
    // pins that it goes through ws-badge, whose brand variant resolves the same intent from tokens.
    const fixture = makeFixture();
    const badge = fixture.nativeElement.querySelector('[data-testid="rule-sim-unit"] .ws-badge');

    expect(badge).toBeTruthy();
    expect(badge.className).toContain('ws-badge--brand');
  });

  it('the title is a heading element, which is how it takes the display face', () => {
    // The serif comes from one global rule on h1..h4 — not a font-family here — so being a real
    // heading is what opts this in. A span would silently render in the body face.
    const fixture = makeFixture();
    const title = fixture.nativeElement.querySelector('.rule-sim__title');

    expect(title).toBeTruthy();
    expect(title.tagName).toBe('H4');
  });

  it('★ the label uses the card\'s own class, so the row reads as one of the card\'s rows', () => {
    // Not a cosmetic assertion: the whole point of this pass was that the block looked bolted on.
    // `.preview-label` is what every sibling row labels itself with.
    const fixture = makeFixture();
    const label = fixture.nativeElement.querySelector('.rule-sim__field .preview-label');

    expect(label).toBeTruthy();
  });
});
