import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ReactiveFormsModule, FormsModule } from '@angular/forms';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError, Subject } from 'rxjs';
import { RuleFormComponent } from './rule-form.component';
import { PlansStore } from '../state/plans.store';
import { PlansApiService } from '../services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { Plan } from '../models/plan.model';
import {
  MeasurementType,
  MeasurementAggregation,
  RateTableType,
  RuleCalculationComponent,
  RuleCalculationOutcome,
  RuleSimulation,
  RuleSimulationBlocker,
} from '../models/rule.model';

/**
 * The commission simulator inside Live Preview.
 *
 * ★★ THE DANGER THE FEATURE INTRODUCES IS NOT A WRONG NUMBER, IT IS A CONFIDENT ONE. A calculator
 * that promises one figure while the system pays another is worse than no calculator, so the tests
 * that matter here are about what the card does NOT do: it does not assemble the cascade itself, it
 * does not answer over a half-written rule, it does not leave a stale figure standing, and it does
 * not let a slow answer to an old question overwrite a fresh one.
 */
describe('RuleFormComponent — commission simulator', () => {
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

  function simulation(overrides: Partial<RuleSimulation> = {}): RuleSimulation {
    return {
      simulated: true,
      blocker: RuleSimulationBlocker.None,
      creditGenerated: true,
      commissionAmount: 100,
      currency: 'EUR',
      steps: [
        { component: RuleCalculationComponent.Trigger, outcome: RuleCalculationOutcome.Applied,
          inputAmount: null, outputAmount: null, operand: null, thresholdAmount: null,
          rateTable: null, attainmentSource: null, tiers: null },
        { component: RuleCalculationComponent.Base, outcome: RuleCalculationOutcome.Applied,
          inputAmount: null, outputAmount: 1200, operand: null, thresholdAmount: null,
          rateTable: null, attainmentSource: null, tiers: null },
        { component: RuleCalculationComponent.Rate, outcome: RuleCalculationOutcome.Applied,
          inputAmount: 1200, outputAmount: 60, operand: 0.05, thresholdAmount: null,
          rateTable: null, attainmentSource: null, tiers: null },
        { component: RuleCalculationComponent.Modifier, outcome: RuleCalculationOutcome.Applied,
          inputAmount: 60, outputAmount: 72, operand: 1.2, thresholdAmount: null,
          rateTable: null, attainmentSource: null, tiers: null },
        { component: RuleCalculationComponent.Cap,
          outcome: RuleCalculationOutcome.AppliedWithoutEffect,
          inputAmount: 72, outputAmount: 72, operand: null, thresholdAmount: 10000,
          rateTable: null, attainmentSource: null, tiers: null },
        { component: RuleCalculationComponent.Floor, outcome: RuleCalculationOutcome.Applied,
          inputAmount: 72, outputAmount: 100, operand: null, thresholdAmount: 100,
          rateTable: null, attainmentSource: null, tiers: null },
      ],
      ...overrides,
    };
  }

  let api: jasmine.SpyObj<PlansApiService>;

  function makeComponent() {
    api = jasmine.createSpyObj<PlansApiService>(
      'PlansApiService', ['simulateRule', 'getTriggerFields', 'getCategoryValues']);
    api.getTriggerFields.and.returnValue(of([]));
    api.getCategoryValues.and.returnValue(of([]));
    api.simulateRule.and.returnValue(of(simulation()));

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

    TestBed.overrideComponent(RuleFormComponent, {
      set: {
        imports: [ReactiveFormsModule, FormsModule, TranslateModule],
        template: `<form [formGroup]="form"></form>`,
      },
    });

    const fixture = TestBed.createComponent(RuleFormComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  /** Fill the form enough that it is valid — the simulator refuses otherwise, by design. */
  function completeForm(comp: RuleFormComponent) {
    comp.form.patchValue({
      name: 'Simulated rule',
      sortOrder: 1,
      measurement: {
        type: MeasurementType.Revenue,
        sourceField: 'amount',
        aggregation: MeasurementAggregation.Sum,
      },
      rateTable: { type: RateTableType.Flat, flatRate: 0.05 },
    });
    comp.form.updateValueAndValidity();
  }

  // ══ ★ It sends what is on the FORM, not what is stored ═════════════════

  it('★★ simulates the definition currently on the form, unsaved', fakeAsync(() => {
    // ★★ THE REASON THE ENDPOINT TAKES A DEFINITION AND NOT AN ID. The card mirrors the form, and the
    // form creates rules as well as editing them. By id, creating would have nothing to simulate,
    // and editing would show the rate just typed beside a figure computed from the rate still in the
    // database — two contradictory numbers in one card.
    const comp = makeComponent();
    completeForm(comp);

    // The user changes the rate and does NOT save.
    comp.form.patchValue({ rateTable: { type: RateTableType.Flat, flatRate: 0.09 } });
    comp.onSimInput(1200);
    tick(300);

    expect(api.simulateRule).toHaveBeenCalled();
    const [planId, request] = api.simulateRule.calls.mostRecent().args;
    expect(planId).toBe(PLAN_ID);
    expect(request.rateTable.flatRate).toBe(0.09, 'the value typed, not the value stored');
    expect(request.amount).toBe(1200);
  }));

  it('a Units rule sends the typed number as the QUANTITY', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);
    comp.form.patchValue({ measurement: { type: MeasurementType.Units } });
    comp.form.patchValue({ rateTable: { type: RateTableType.Flat, flatRate: 5 } });

    comp.onSimInput(3);
    tick(300);

    const [, request] = api.simulateRule.calls.mostRecent().args;
    expect(request.quantity).toBe(3);
  }));

  // ══ ★ Nothing over a half-written rule ════════════════════════════════

  it('★ an invalid form does not simulate at all', fakeAsync(() => {
    // ★ A COMMISSION COMPUTED FROM A CONFIGURATION NOBODY HAS FINISHED WRITING is not a preview; it
    // is a figure that looks like one, which is worse than showing nothing.
    const comp = makeComponent();
    comp.form.patchValue({ name: '' });
    comp.form.updateValueAndValidity();

    comp.onSimInput(1200);
    tick(300);

    expect(comp.canSimulate()).toBeFalse();
    expect(api.simulateRule).not.toHaveBeenCalled();
    expect(comp.simulation()).toBeNull();
  }));

  it('clearing the box drops the previous answer instead of leaving it on screen', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    comp.onSimInput(1200);
    tick(300);
    expect(comp.simulation()).not.toBeNull();

    comp.onSimInput(null);
    tick(300);
    expect(comp.simulation()).withContext('a figure for an amount nobody typed').toBeNull();
  }));

  // ══ ★★ Out-of-order responses ═════════════════════════════════════════

  it('★★ discards a late answer to a question that was already replaced', fakeAsync(() => {
    // ★★ WITHOUT THIS, a slow request for 1,200 lands after a fast one for 5,000 and leaves a
    // commission belonging to neither amount on screen, under the number the user is looking at.
    const comp = makeComponent();
    completeForm(comp);

    const slow = new Subject<RuleSimulation>();
    const fast = new Subject<RuleSimulation>();

    api.simulateRule.and.returnValue(slow.asObservable());
    comp.onSimInput(1200);
    tick(300);

    api.simulateRule.and.returnValue(fast.asObservable());
    comp.onSimInput(5000);
    tick(300);

    // The second question is answered first…
    fast.next(simulation({ commissionAmount: 250 }));
    expect(comp.simulation()!.commissionAmount).toBe(250);

    // …and then the first one finally replies. It must be dropped.
    slow.next(simulation({ commissionAmount: 100 }));
    expect(comp.simulation()!.commissionAmount)
      .toBe(250, 'the stale answer must not overwrite the current one');
  }));

  it('debounces: typing three digits in a row issues one request', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    comp.onSimInput(1);
    tick(100);
    comp.onSimInput(12);
    tick(100);
    comp.onSimInput(120);
    tick(300);

    expect(api.simulateRule).toHaveBeenCalledTimes(1);
  }));

  // ══ ★ Failure is visible and never silent ═════════════════════════════

  it('★ a failed simulation is shown, and no stale figure is left beside it', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    comp.onSimInput(1200);
    tick(300);
    expect(comp.simulation()).not.toBeNull();

    api.simulateRule.and.returnValue(throwError(() => new Error('down')));
    comp.onSimInput(2400);
    tick(300);

    expect(comp.simErrorKey()).toBeTruthy('the failure is visible');
    expect(comp.simulation()).withContext('never a number from the previous question next to an error').toBeNull();
    expect(comp.simLoading()).toBeFalse();
  }));

  /**
   * ★★ THE PANEL PRINTED "RateTableRateAboveMaximum" ON A USER'S SCREEN, AND THIS IS THE TEST THAT
   * SAYS IT MAY NOT AGAIN.
   *
   * Two halves failed at once. On the server, `DomainCodedException` is a `DomainException`, so
   * `SimulateRuleHandler`'s catch swallowed it and flattened it into a `Failure(ex.Message)` — and
   * that Message is the CODE, because the type passes it to `base()` to keep logs readable. On the
   * client, `extractApiError` returns `err.error.message` verbatim, so the identifier went straight
   * onto the card. The toast beside it translated correctly the whole time; only this panel did not,
   * because it never ran the code through the whitelist.
   *
   * It stayed hidden until the rate-magnitude guard moved into `Plan.AddRule`: before that, every
   * coded refusal came from `RateTableRequest.ToDomain`, which the simulator does not call.
   */
  it('★★ a coded refusal becomes a translated sentence, never the raw code', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    api.simulateRule.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 422,
      error: { status: 422, code: 'RateTableRateAboveMaximum', parameters: { rate: 4, maximum: 1 } },
    })));

    comp.onSimInput(50000);
    tick(300);

    expect(comp.simErrorKey()).toBe('PLANS.RATE_TABLE_ERR_RATE_TOO_HIGH_FLAT');
    expect(comp.simErrorKey()).not.toContain('RateTableRateAboveMaximum');

    // Without the parameters the reader is shown "{{rate}}"; the key alone is not the fix.
    expect(comp.simErrorParams()).toEqual({ rate: 4, maximum: 1 });
  }));

  it('a coded refusal for a TIER names the tier, and its params reach the sentence', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    api.simulateRule.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 422,
      error: {
        status: 422,
        code: 'RateTableRateAboveMaximum',
        parameters: { tierNumber: 2, rate: 7, maximum: 1 },
      },
    })));

    comp.onSimInput(50000);
    tick(300);

    expect(comp.simErrorKey()).toBe('PLANS.RATE_TABLE_ERR_RATE_TOO_HIGH_TIER');
    expect(comp.simErrorParams()!['tierNumber']).toBe(2);
  }));

  /**
   * ★ AN UNKNOWN CODE IS NOT ASSUMED TO BE A RATE-TABLE PROBLEM, and above all is not spelled out.
   * The same rule the save toast follows: fall through to the plain path rather than describe a
   * refusal this build does not understand as a bad ladder.
   */
  it('★ an unrecognised code falls through without ever printing the identifier', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    api.simulateRule.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 422,
      error: { status: 422, code: 'SomethingThisBuildHasNeverHeardOf', parameters: {} },
    })));

    comp.onSimInput(1200);
    tick(300);

    expect(comp.simErrorKey()).toBeTruthy('the failure is still visible');
    expect(comp.simErrorKey()).not.toContain('SomethingThisBuildHasNeverHeardOf');
    expect(comp.simErrorParams()).toBeNull();
  }));

  it('the parameters are cleared when the error is, so they cannot leak into the next one', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    api.simulateRule.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 422,
      error: { status: 422, code: 'RateTableRateAboveMaximum', parameters: { rate: 4, maximum: 1 } },
    })));
    comp.onSimInput(50000);
    tick(300);
    expect(comp.simErrorParams()).not.toBeNull();

    api.simulateRule.and.returnValue(of(simulation()));
    comp.retrySimulation();
    tick(300);

    expect(comp.simErrorKey()).toBeNull();
    expect(comp.simErrorParams()).toBeNull();
  }));

  it('retry re-issues the request', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    api.simulateRule.and.returnValue(throwError(() => new Error('down')));
    comp.onSimInput(1200);
    tick(300);
    expect(comp.simErrorKey()).toBeTruthy();

    api.simulateRule.and.returnValue(of(simulation()));
    comp.retrySimulation();
    tick(300);

    expect(comp.simErrorKey()).toBeNull();
    expect(comp.simulation()!.commissionAmount).toBe(100);
  }));

  // ══ ★ The steps are the server's ══════════════════════════════════════

  it('★★ keeps the server order — cap before floor — and never sorts it', fakeAsync(() => {
    // ★★ THE FAILURE THIS WHOLE FEATURE WAS DESIGNED AROUND. The engine applies the floor AFTER the
    // cap, so a floor above a cap wins. A card that rebuilt this sequence "logically" would show a
    // total that is right beside a story that is wrong, and teach the reader a model of the product
    // that does not match what pays them.
    const comp = makeComponent();
    completeForm(comp);

    comp.onSimInput(1200);
    tick(300);

    expect(comp.simulation()!.steps.map((s) => s.component)).toEqual([
      RuleCalculationComponent.Trigger,
      RuleCalculationComponent.Base,
      RuleCalculationComponent.Rate,
      RuleCalculationComponent.Modifier,
      RuleCalculationComponent.Cap,
      RuleCalculationComponent.Floor,
    ]);
    expect(comp.simulation()!.commissionAmount).toBe(100, 'the floor won over the cap');
  }));

  it('an attainment rule that the server refuses is reported, not answered', fakeAsync(() => {
    const comp = makeComponent();
    completeForm(comp);

    api.simulateRule.and.returnValue(of(simulation({
      simulated: false,
      blocker: RuleSimulationBlocker.AttainmentContextRequired,
      creditGenerated: false,
      commissionAmount: null,
      steps: [],
    })));

    comp.onSimInput(1200);
    tick(300);

    expect(comp.simulation()!.simulated).toBeFalse();
    expect(comp.simulation()!.blocker).toBe(RuleSimulationBlocker.AttainmentContextRequired);
    expect(comp.simulation()!.commissionAmount).toBeNull();
  }));

  // ══ ★★ The blocked state names what is missing ═══════════════════════

  it('★★ says WHICH field is incomplete, not "finish configuring the rule"', () => {
    // ★★ THE DEFECT THIS REPLACES. The old message left the user in front of six sections —
    // measurement, rate table, trigger, modifier, cap, floor — guessing which one it meant. That is
    // the same uselessness as "I could not find it": true, and it makes the person redo a search the
    // software already did.
    const comp = makeComponent();
    comp.form.patchValue({ name: '' });
    comp.form.updateValueAndValidity();

    expect(comp.canSimulate()).toBeFalse();
    expect(comp.simBlockedFieldKey()).toBe('PLANS.FIELD_RULE_NAME');
  });

  it('★ the named field FOLLOWS the form, changing as the invalid control changes', () => {
    // ★ THE NAME IS DERIVED, NOT WRITTEN DOWN. It comes from Angular's own validity walked in
    // declaration order, so it tracks the screen's reading order and corrects itself — nothing has
    // to be kept in sync by hand when a field is added or renamed.
    const comp = makeComponent();
    completeForm(comp);
    expect(comp.simBlockedFieldKey()).toBeNull();

    comp.form.patchValue({ name: '' });
    comp.form.updateValueAndValidity();
    expect(comp.simBlockedFieldKey()).toBe('PLANS.FIELD_RULE_NAME');

    // Fix the name, break something later in the form: the message must move on.
    //
    // ★ THE CAP SECTION HAS TO BE SWITCHED ON FOR ITS AMOUNT TO MATTER. This test used to leave it
    // off and rely on the control's own `min(0)` validator to invalidate the whole form — which was
    // the old coupling in miniature: a value that is never sent (`cap: hasCap ? {...} : null`) was
    // blocking a calculation it takes no part in.
    comp.form.patchValue({ name: 'Named now', hasCap: true });
    comp.form.get('cap.amount')!.setValue(-5);
    comp.form.updateValueAndValidity();
    expect(comp.simBlockedFieldKey()).toBe('PLANS.FIELD_CAP_AMOUNT');
  });

  it('★ the FIRST invalid field wins when several are broken, in form order', () => {
    // Otherwise the message would point at whichever field the walker happened to reach first, and
    // would send the reader to the bottom of the form to fix something above it.
    const comp = makeComponent();
    completeForm(comp);

    comp.form.patchValue({ name: '' });
    comp.form.get('cap.amount')!.setValue(-5);
    comp.form.updateValueAndValidity();

    expect(comp.simBlockedFieldKey()).toBe('PLANS.FIELD_RULE_NAME', 'name is declared first');
  });

  it('a complete form names nothing and unblocks the input', () => {
    const comp = makeComponent();
    completeForm(comp);

    expect(comp.canSimulate()).toBeTrue();
    expect(comp.simBlockedFieldKey()).toBeNull();
  });

  // ══ ★★ Read-only is the case that matters most ════════════════════════

  it('★★ a READ-ONLY rule with a complete definition can be simulated', () => {
    // ★★ THE DEFECT THIS FIXES, AND IT BIT ON THE SCREEN WHERE THE FEATURE IS MOST USEFUL. A rule
    // belonging to an active plan cannot be edited, so simulating is the ONLY way to find out what
    // it pays — and that is precisely the screen where the simulator refused to run.
    //
    // ★ THE CAUSE WAS `form.valid`. Angular gives a disabled FormGroup status DISABLED, and `valid`
    // is `status === VALID`, so a disabled form is never valid however complete it is. The check now
    // asks the DEFINITION — `getRawValue()`, which includes disabled controls — instead of the state
    // of the controls.
    const comp = makeComponent();
    completeForm(comp);

    comp.form.disable({ emitEvent: false });
    comp.readOnly.set(true);

    expect(comp.form.valid).withContext('a disabled form is never valid — that was the trap').toBeFalse();
    expect(comp.canSimulate()).withContext('but the definition is complete, so it simulates').toBeTrue();
    expect(comp.simBlockedFieldKey()).toBeNull();
  });

  it('★ it recomputes when the form is locked, even though disable() emits no value event', () => {
    // ★ THE SECOND HALF OF THE BUG. `disable({ emitEvent: false })` never fires valueChanges, so a
    // computed reading only the form value would not even re-run. Depending on readOnly() is what
    // makes the state settle.
    const comp = makeComponent();
    completeForm(comp);
    expect(comp.canSimulate()).toBeTrue();

    comp.form.disable({ emitEvent: false });
    comp.readOnly.set(true);

    expect(comp.canSimulate()).toBeTrue();
  });

  it('a read-only rule that is genuinely incomplete still names what is missing', () => {
    const comp = makeComponent();
    completeForm(comp);
    comp.form.get('rateTable.flatRate')!.setValue(null as unknown as number);

    comp.form.disable({ emitEvent: false });
    comp.readOnly.set(true);

    expect(comp.canSimulate()).toBeFalse();
    expect(comp.simBlockedFieldKey()).toBe('PLANS.FIELD_FLAT_RATE');
  });

  // ══ ★ The trigger with no conditions ══════════════════════════════════

  it('★ a trigger switched on with ZERO conditions does not block', () => {
    // ★ BECAUSE IT IS NOT MISSING ANYTHING. The domain reads an empty condition list as
    // `Trigger.Always` (EvaluateTrigger returns true when Count == 0), so the rule matches every
    // transaction and simulates fine. Blocking here would invent a requirement the product does not
    // have — the same false block this work item removes.
    const comp = makeComponent();
    completeForm(comp);
    comp.form.patchValue({ hasTrigger: true });
    comp.form.updateValueAndValidity();

    expect(comp.canSimulate()).toBeTrue();
    expect(comp.simBlockedFieldKey()).toBeNull();
  });

  it('a trigger condition with no FIELD does block, and says it is the trigger', () => {
    // That one the engine cannot honour, and the server rejects it too.
    const comp = makeComponent();
    completeForm(comp);
    comp.form.patchValue({ hasTrigger: true });
    comp.addCondition();
    comp.form.updateValueAndValidity();

    expect(comp.simBlockedFieldKey()).toBe('PLANS.RULE_SECTION_TRIGGER');
  });

  // ══ ★ An empty box is not a zero ══════════════════════════════════════

  it('★ a CLEARED number field blocks instead of being read as zero', () => {
    // ★ `Number('')` is 0 and `Number(null)` is 0. Without an explicit emptiness check, a field the
    // user has just cleared would simulate as a perfectly good zero.
    const comp = makeComponent();
    completeForm(comp);
    comp.form.patchValue({ hasCap: true });
    comp.form.get('cap.amount')!.setValue(null as unknown as number);
    comp.form.updateValueAndValidity();

    expect(comp.simBlockedFieldKey()).toBe('PLANS.FIELD_CAP_AMOUNT');

    comp.form.get('cap.amount')!.setValue(0);
    comp.form.updateValueAndValidity();
    expect(comp.simBlockedFieldKey()).withContext('a real zero is a legitimate cap').toBeNull();
  });

  it('every key the blocked message can name actually exists in the bundle', async () => {
    // ★ A message that renders its own i18n key at somebody is worse than the vague sentence it
    // replaced. Every reachable key is driven through the real component and checked against the
    // real EN bundle rather than a fixture.
    const comp = makeComponent();
    const bundle = await fetch('/assets/i18n/en.json').then((r) => r.json());

    const breakIt: ReadonlyArray<() => void> = [
      () => comp.form.patchValue({ name: '' }),
      () => comp.form.get('rateTable.flatRate')!.setValue(null as unknown as number),
      () => { comp.form.patchValue({ hasCap: true }); comp.form.get('cap.amount')!.setValue(null as unknown as number); },
      () => { comp.form.patchValue({ hasFloor: true }); comp.form.get('floor.amount')!.setValue(null as unknown as number); },
      () => { comp.form.patchValue({ hasModifier: true }); comp.form.get('modifier.factor')!.setValue(null as unknown as number); },
    ];

    for (const breakOne of breakIt) {
      completeForm(comp);
      breakOne();
      comp.form.updateValueAndValidity();

      const key = comp.simBlockedFieldKey();
      expect(key).withContext('a broken definition must name something').toBeTruthy();
      expect(bundle.PLANS[key!.slice('PLANS.'.length)])
        .withContext(`${key} is missing from en.json`).toBeDefined();
    }
  });

  it('every step component maps to a translation key — none renders raw', () => {
    const comp = makeComponent();
    const components = [
      RuleCalculationComponent.Trigger, RuleCalculationComponent.Base,
      RuleCalculationComponent.Rate, RuleCalculationComponent.Modifier,
      RuleCalculationComponent.Cap, RuleCalculationComponent.Floor,
    ];

    for (const c of components) {
      expect(comp.stepLabelKey(c)).toMatch(/^PLANS\./);
    }
  });
});
