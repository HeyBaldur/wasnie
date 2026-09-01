import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { RuleFormComponent } from './rule-form.component';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { Plan } from '../models/plan.model';
import {
  Rule,
  MeasurementType,
  MeasurementAggregation,
  RateTableType,
  ModifierType,
  CapScope,
} from '../models/rule.model';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const PLAN_ID = 'plan-1';
const RULE_ID = 'rule-1';

/** Simulate what the API actually returns: enums as string names. */
function makeApiRule(overrides: Partial<Rule> = {}): Rule {
  return {
    id: RULE_ID,
    name: 'Rule Test #1',
    sortOrder: 1,
    isActive: true,
    trigger: null,
    modifier: null,
    cap: null,
    floor: null,
    // The endpoint sends these on every rule; a fixture that omits them is not what production sees.
    stoppedAt: null,
    stoppedBy: null,
    stopReason: null,
    measurement: {
      _schema: 1 as const,
      type: 'Revenue' as unknown as MeasurementType,
      sourceField: 'amount',
      aggregation: 'Sum' as unknown as MeasurementAggregation,
    },
    rateTable: {
      _schema: 1 as const,
      type: 'Flat' as unknown as RateTableType,
      flatRate: 0.05,
      tiers: null,
      attainmentTiers: null,
      splitAtQuota: false,
    },
    ...overrides,
  };
}

function makePlan(rule: Rule): Plan {
  return {
    id: PLAN_ID,
    tenantId: 'tenant-1',
    name: 'Test Plan',
    description: '',
    version: 1,
    status: 'Draft',        // Draft so form is NOT disabled
    effectiveStart: '2024-01-01',
    effectiveEnd: '2024-12-31',
    currency: 'USD',
    createdAt: '2024-01-01T00:00:00Z',
    createdBy: 'user-1',
    rules: [rule],
    // Opted out of clawbacks, like every plan until someone configures a maturation window.
    activeAssignmentCount: 0,
    clawbackMaturationDays: null,
    clawbackCapPercent: null,
  };
}

// ---------------------------------------------------------------------------
// Test suite
// ---------------------------------------------------------------------------

describe('RuleFormComponent — MeasurementType picker filter (V1)', () => {
  function configureMinimalModule(plan: Plan): void {
    const planSignal = signal<Plan | null>(plan);
    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PlansStore,
          useValue: { selectedPlan: planSignal as unknown as PlansStore['selectedPlan'], loadPlan: jasmine.createSpy('loadPlan').and.returnValue(Promise.resolve()) },
        },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (k: string) => k === 'planId' ? PLAN_ID : null } } } },
      ],
    });
    TestBed.overrideComponent(RuleFormComponent, { set: { imports: [ReactiveFormsModule, TranslateModule], template: `<form [formGroup]="form"></form>` } });
  }

  afterEach(() => TestBed.resetTestingModule());

  it('measurementTypeOptions contains exactly Revenue and Units', () => {
    configureMinimalModule(makePlan(makeApiRule()));
    const comp = TestBed.createComponent(RuleFormComponent).componentInstance;
    expect(comp.measurementTypeOptions.length).toBe(2);
    expect(comp.measurementTypeOptions.map(o => o.value)).toContain(MeasurementType.Revenue);
    expect(comp.measurementTypeOptions.map(o => o.value)).toContain(MeasurementType.Units);
  });

  it('measurementTypeOptions does not contain Margin, Attainment, or Custom', () => {
    configureMinimalModule(makePlan(makeApiRule()));
    const comp = TestBed.createComponent(RuleFormComponent).componentInstance;
    const values = comp.measurementTypeOptions.map(o => o.value);
    expect(values).not.toContain(MeasurementType.Margin);
    expect(values).not.toContain(MeasurementType.Attainment);
    expect(values).not.toContain(MeasurementType.Custom);
  });
});

// ---------------------------------------------------------------------------
// Floor above cap — a contradictory combination the engine accepts silently
// ---------------------------------------------------------------------------
// The engine applies modifier → cap → floor, so the floor runs LAST: a floor above the cap lifts the
// commission back over the ceiling and the cap stops changing any outcome. The form must say so.

describe('RuleFormComponent — floor above cap warning', () => {
  function makeComponent() {
    const planSignal = signal<Plan | null>(makePlan(makeApiRule()));
    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PlansStore,
          useValue: {
            selectedPlan: planSignal as unknown as PlansStore['selectedPlan'],
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
      set: { imports: [ReactiveFormsModule, TranslateModule], template: `<form [formGroup]="form"></form>` },
    });
    const fixture = TestBed.createComponent(RuleFormComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  function set(comp: RuleFormComponent, hasCap: boolean, cap: number, hasFloor: boolean, floor: number) {
    comp.form.patchValue({ hasCap, cap: { amount: cap }, hasFloor, floor: { amount: floor } });
  }

  it('warns when the floor is above the cap', () => {
    const comp = makeComponent();
    set(comp, true, 200, true, 500);
    expect(comp.floorExceedsCap()).toBeTrue();
  });

  it('stays quiet when the floor is below the cap', () => {
    const comp = makeComponent();
    set(comp, true, 500, true, 200);
    expect(comp.floorExceedsCap()).toBeFalse();
  });

  it('stays quiet when floor and cap are equal — pinned, not contradictory', () => {
    const comp = makeComponent();
    set(comp, true, 300, true, 300);
    expect(comp.floorExceedsCap()).toBeFalse();
  });

  it('says nothing when either section is switched off', () => {
    const comp = makeComponent();
    set(comp, false, 200, true, 500);
    expect(comp.floorExceedsCap()).withContext('no cap to contradict').toBeFalse();

    set(comp, true, 200, false, 500);
    expect(comp.floorExceedsCap()).withContext('no floor to apply').toBeFalse();
  });

  it('treats a cap of zero as "no cap set yet" rather than a cap of nothing', () => {
    // The field defaults to 0 the moment the section is toggled on; warning on that would fire
    // before the user has typed anything.
    const comp = makeComponent();
    set(comp, true, 0, true, 500);
    expect(comp.floorExceedsCap()).toBeFalse();
  });
});


/**
 * The flat rate defends itself against the percentage-convention mistake.
 *
 * ★ THE ENGINE'S TRUTH, WHICH THESE ARE WRITTEN AGAINST: `CommissionCalculator` computes
 * `baseAmount.Multiply(FlatRate)`. The stored number is a MULTIPLIER — 0.05 is 5%, and 100 is ten
 * thousand per cent. A user typing 100 for "one hundred per cent" is the money error this exists to
 * catch, and nothing but the form is standing there when they do it.
 */
describe('RuleFormComponent — the flat rate protects itself', () => {
  function makeComponent(): RuleFormComponent {
    const planSignal = signal<Plan | null>(makePlan(makeApiRule()));
    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PlansStore,
          useValue: {
            selectedPlan: planSignal as unknown as PlansStore['selectedPlan'],
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
      set: { imports: [ReactiveFormsModule, TranslateModule], template: `<form [formGroup]="form"></form>` },
    });
    const fixture = TestBed.createComponent(RuleFormComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  function setRate(comp: RuleFormComponent, flatRate: number | undefined): void {
    comp.form.patchValue({ rateTable: { flatRate } });
  }

  it('★ echoes back what the number actually means', () => {
    // The static hint has always said "0.05 = 5%". A hint is also the easiest thing on a form to skim
    // past, so this says it about the number in front of the user.
    const comp = makeComponent();

    setRate(comp, 0.05);
    expect(comp.flatRatePercent()).toBe(5);

    setRate(comp, 0.025);
    expect(comp.flatRatePercent()).toBe(2.5);

    setRate(comp, 1);
    expect(comp.flatRatePercent()).toBe(100);
  });

  it('★ warns when the rate would pay out the whole sale or more', () => {
    // ★ THE MISTAKE ITSELF. Someone typing 100 for "one hundred per cent" gets ten thousand — and the
    // form is the only thing between them and a pay run.
    const comp = makeComponent();

    setRate(comp, 100);
    expect(comp.flatRatePercent()).toBe(10000);
    expect(comp.flatRateLooksMistaken()).toBeTrue();

    // Exactly 100% is the boundary and it warns: the commission equals the entire transaction.
    setRate(comp, 1);
    expect(comp.flatRateLooksMistaken()).toBeTrue();
  });

  it('stays quiet for ordinary rates, including generous ones', () => {
    // ★ A warning that fires on real values is one people learn to click past. 50% is a high rate, not
    // a mistake.
    const comp = makeComponent();

    for (const rate of [0.05, 0.1, 0.25, 0.5, 0.999]) {
      setRate(comp, rate);
      expect(comp.flatRateLooksMistaken()).withContext(`${rate * 100}% is legitimate`).toBeFalse();
    }
  });

  it('says nothing at all before a rate has been typed', () => {
    const comp = makeComponent();

    setRate(comp, undefined);
    expect(comp.flatRatePercent()).withContext('nothing to echo yet').toBeNull();
    expect(comp.flatRateLooksMistaken()).toBeFalse();
  });

  it('★ WARNS, it does not BLOCK — the unusual value is still saveable', () => {
    // A rule paying the entire amount is unusual, not impossible, and a form that refuses a legitimate
    // configuration sends the user to support. The warning is advice; the control stays valid.
    const comp = makeComponent();
    setRate(comp, 1.5);

    expect(comp.flatRateLooksMistaken()).toBeTrue();
    expect(comp.form.get('rateTable.flatRate')?.errors)
      .withContext('advice, not a validation failure').toBeNull();
    expect(comp.form.get('rateTable.flatRate')?.valid).toBeTrue();
  });

  it('★ the value the form carries is UNCHANGED — this is presentation only', () => {
    // ★ The engine reads this number. Nothing here may convert, round or rescale it: the field still
    // holds exactly what was typed, and the percentage lives only in what is displayed.
    const comp = makeComponent();

    setRate(comp, 0.05);
    expect(comp.form.get('rateTable.flatRate')?.value).toBe(0.05);

    setRate(comp, 100);
    expect(comp.form.get('rateTable.flatRate')?.value).toBe(100);
  });

  it('says nothing about percentages in Units mode, where the field is money per unit', () => {
    // ★ THE SAME FIELD MEANS TWO THINGS. Under Units the engine multiplies it by a QUANTITY, so it is
    // euros per unit — and "2.00 = 200%" would be a fresh piece of nonsense in a form that exists to
    // remove one.
    const comp = makeComponent();
    comp.form.patchValue({ measurement: { type: MeasurementType.Units } });
    setRate(comp, 2);

    expect(comp.isUnitsMode()).toBeTrue();
    expect(comp.flatRatePercent()).withContext('a per-unit amount is not a percentage').toBeNull();
    expect(comp.flatRateLooksMistaken()).withContext('€2.00 per unit is ordinary').toBeFalse();
  });
});

describe('RuleFormComponent — enum rehydration from string API values', () => {
  let storeMock: Partial<PlansStore>;

  function configureModule(plan: Plan): void {
    const planSignal = signal<Plan | null>(plan);
    storeMock = {
      selectedPlan: planSignal as unknown as PlansStore['selectedPlan'],
      loadPlan: jasmine.createSpy('loadPlan').and.returnValue(Promise.resolve()),
    };

    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PlansStore, useValue: storeMock },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (k: string) => k === 'planId' ? PLAN_ID : k === 'ruleId' ? RULE_ID : null,
              },
            },
          },
        },
      ],
    });
    // Override the component so the template only uses ReactiveFormsModule — no
    // AppShellComponent or heavy nested components that would require their own
    // provider trees in unit tests.
    TestBed.overrideComponent(RuleFormComponent, {
      set: {
        imports: [ReactiveFormsModule, TranslateModule],
        template: `<form [formGroup]="form"></form>`,
      },
    });
  }

  afterEach(() => TestBed.resetTestingModule());

  // -------------------------------------------------------------------------
  // Bug 1: Measurement / Aggregation dropdowns
  // -------------------------------------------------------------------------

  it('coerces string "Revenue" → numeric MeasurementType.Revenue in the form', fakeAsync(() => {
    configureModule(makePlan(makeApiRule()));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('measurement.type')?.value).toBe(MeasurementType.Revenue);
  }));

  it('coerces string "Sum" → numeric MeasurementAggregation.Sum in the form', fakeAsync(() => {
    configureModule(makePlan(makeApiRule()));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('measurement.aggregation')?.value).toBe(MeasurementAggregation.Sum);
  }));

  // -------------------------------------------------------------------------
  // Bug 2: Rate Table type — rateTableType() signal and Live Preview
  // -------------------------------------------------------------------------

  it('rateTableType() is Flat (0) when API returns string "Flat"', fakeAsync(() => {
    configureModule(makePlan(makeApiRule()));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.rateTableType()).toBe(RateTableType.Flat);
  }));

  it('form control rateTable.type is numeric 0 (Flat) — not string "Flat"', fakeAsync(() => {
    configureModule(makePlan(makeApiRule()));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('rateTable.type')?.value).toBe(0);
  }));

  it('flatRate is populated when rate table type is Flat', fakeAsync(() => {
    configureModule(makePlan(makeApiRule()));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('rateTable.flatRate')?.value).toBeCloseTo(0.05);
  }));

  // -------------------------------------------------------------------------
  // Tiered rate table
  // -------------------------------------------------------------------------

  it('rateTableType() is Tiered (1) when API returns string "Tiered"', fakeAsync(() => {
    const tieredRule = makeApiRule({
      rateTable: {
        _schema: 1 as const,
        type: 'Tiered' as unknown as RateTableType,
        flatRate: null,
        tiers: [
          { from: 0, to: 500, rate: 0.03 },
          { from: 500, to: null, rate: 0.05 },
        ],
        attainmentTiers: null,
        splitAtQuota: false,
      },
    });
    configureModule(makePlan(tieredRule));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.rateTableType()).toBe(RateTableType.Tiered);
  }));

  it('loads tier rows into tiersArray when rate table is Tiered', fakeAsync(() => {
    const tieredRule = makeApiRule({
      rateTable: {
        _schema: 1 as const,
        type: 'Tiered' as unknown as RateTableType,
        flatRate: null,
        tiers: [
          { from: 0, to: 500, rate: 0.03 },
          { from: 500, to: null, rate: 0.05 },
        ],
        attainmentTiers: null,
        splitAtQuota: false,
      },
    });
    configureModule(makePlan(tieredRule));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.tiersArray.length).toBe(2);
    expect(comp.tiersArray.at(0).value).toEqual({ from: 0, to: 500, rate: 0.03 });
    expect(comp.tiersArray.at(1).value).toEqual({ from: 500, to: null, rate: 0.05 });
  }));

  // -------------------------------------------------------------------------
  // AttainmentBased rate table
  // -------------------------------------------------------------------------

  it('rateTableType() is AttainmentBased (2) when API returns string "AttainmentBased"', fakeAsync(() => {
    const attRule = makeApiRule({
      rateTable: {
        _schema: 1 as const,
        type: 'AttainmentBased' as unknown as RateTableType,
        flatRate: null,
        tiers: null,
        attainmentTiers: [
          { attainmentFrom: 0, attainmentTo: 0.8, rate: 0.02 },
          { attainmentFrom: 0.8, attainmentTo: null, rate: 0.05 },
        ],
        splitAtQuota: false,
      },
    });
    configureModule(makePlan(attRule));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.rateTableType()).toBe(RateTableType.AttainmentBased);
  }));

  it('loads attainment tier rows into attainmentTiersArray', fakeAsync(() => {
    const attRule = makeApiRule({
      rateTable: {
        _schema: 1 as const,
        type: 'AttainmentBased' as unknown as RateTableType,
        flatRate: null,
        tiers: null,
        attainmentTiers: [
          { attainmentFrom: 0, attainmentTo: 0.8, rate: 0.02 },
          { attainmentFrom: 0.8, attainmentTo: null, rate: 0.05 },
        ],
        splitAtQuota: false,
      },
    });
    configureModule(makePlan(attRule));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.attainmentTiersArray.length).toBe(2);
  }));

  // -------------------------------------------------------------------------
  // Modifier enum coercion
  // -------------------------------------------------------------------------

  it('coerces modifier type string "Multiplier" → numeric ModifierType.Multiplier', fakeAsync(() => {
    const ruleWithModifier = makeApiRule({
      modifier: {
        _schema: 1 as const,
        id: 'mod-1',
        name: 'My Modifier',
        type: 'Multiplier' as unknown as ModifierType,
        factor: 1.5,
        trigger: null,
      },
    });
    configureModule(makePlan(ruleWithModifier));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('modifier.type')?.value).toBe(ModifierType.Multiplier);
  }));

  // -------------------------------------------------------------------------
  // Cap scope enum coercion
  // -------------------------------------------------------------------------

  it('coerces cap scope string "PerPeriod" → numeric CapScope.PerPeriod', fakeAsync(() => {
    const ruleWithCap = makeApiRule({
      cap: {
        _schema: 1 as const,
        amount: { amount: 1000, currency: 'USD' },
        scope: 'PerPeriod' as unknown as CapScope,
      },
    });
    configureModule(makePlan(ruleWithCap));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('cap.scope')?.value).toBe(CapScope.PerPeriod);
  }));

  // -------------------------------------------------------------------------
  // splitAtQuota hydration and serialization
  // -------------------------------------------------------------------------

  it('splitAtQuota defaults to false when loading a Flat rule', fakeAsync(() => {
    configureModule(makePlan(makeApiRule()));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('rateTable.splitAtQuota')?.value).toBe(false);
  }));

  it('splitAtQuota is populated from API value true when loading an AttainmentBased rule', fakeAsync(() => {
    const splitRule = makeApiRule({
      rateTable: {
        _schema: 1 as const,
        type: 'AttainmentBased' as unknown as RateTableType,
        flatRate: null,
        tiers: null,
        attainmentTiers: [
          { attainmentFrom: 0, attainmentTo: 1.0, rate: 0.04 },
          { attainmentFrom: 1.0, attainmentTo: null, rate: 0.07 },
        ],
        splitAtQuota: true,
      },
    });
    configureModule(makePlan(splitRule));
    const fixture = TestBed.createComponent(RuleFormComponent);
    const comp = fixture.componentInstance;

    comp.ngOnInit();
    tick();

    expect(comp.form.get('rateTable.splitAtQuota')?.value).toBe(true);
  }));
});

// ---------------------------------------------------------------------------
// Category value picker (WI — condition value on `category` is chosen, not typed)
// ---------------------------------------------------------------------------

describe('RuleFormComponent — category value picker', () => {
  const CATEGORY_FIELD_DEF = {
    field: 'category',
    valueType: 'String',
    operators: [
      { operator: 'Equal', usesSet: false },
      { operator: 'NotEqual', usesSet: false },
      { operator: 'In', usesSet: true },
      { operator: 'NotIn', usesSet: true },
    ],
  };
  const SKU_FIELD_DEF = {
    field: 'productsku',
    valueType: 'String',
    operators: [
      { operator: 'Equal', usesSet: false },
      { operator: 'In', usesSet: true },
    ],
  };

  let httpMock: HttpTestingController;

  function setup(plan: Plan, ruleId: string | null): RuleFormComponent {
    const planSignal = signal<Plan | null>(plan);
    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PlansStore,
          useValue: {
            selectedPlan: planSignal as unknown as PlansStore['selectedPlan'],
            loadPlan: jasmine.createSpy('loadPlan').and.returnValue(Promise.resolve()),
          },
        },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (k: string) => k === 'planId' ? PLAN_ID : k === 'ruleId' ? ruleId : null },
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

  /** Flush the two catalog requests ngOnInit fires. Categories default to the tenant's three. */
  function flushCatalogs(categories: string[] = ['Laptops', 'Servers', 'Calculators']): void {
    httpMock.expectOne('/api/plans/trigger-fields').flush([CATEGORY_FIELD_DEF, SKU_FIELD_DEF]);
    httpMock.expectOne('/api/plans/category-values').flush(categories);
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  // (a) A condition on `category` uses the picker, not a free-text input.
  it('uses the category picker (not free text) for a condition on category', fakeAsync(() => {
    const comp = setup(makePlan(makeApiRule()), null);
    comp.ngOnInit();
    tick();
    flushCatalogs();

    comp.addCondition();
    comp.conditionsArray.at(0).get('field')?.setValue('category');
    tick();

    expect(comp.isCategoryField(0)).toBeTrue();
    expect(comp.useCategoryPicker(0)).toBeTrue();   // → chips render
    expect(comp.customValueAt(0)).toBeFalse();       // → not free text by default
  }));

  // (e — no regression) A non-category field keeps its existing free-text input.
  it('does NOT use the picker for a non-category field (productsku)', fakeAsync(() => {
    const comp = setup(makePlan(makeApiRule()), null);
    comp.ngOnInit();
    tick();
    flushCatalogs();

    comp.addCondition();
    comp.conditionsArray.at(0).get('field')?.setValue('productsku');
    tick();

    expect(comp.isCategoryField(0)).toBeFalse();
    expect(comp.useCategoryPicker(0)).toBeFalse();   // → existing input path
  }));

  // (b) With In, the value is a multi-select picker (bound to the CSV `valueSet` the form submits).
  it('shows the multi-select category picker when the operator is In', fakeAsync(() => {
    const comp = setup(makePlan(makeApiRule()), null);
    comp.ngOnInit();
    tick();
    flushCatalogs();

    comp.addCondition();
    comp.conditionsArray.at(0).get('field')?.setValue('category');
    comp.conditionsArray.at(0).get('operator')?.setValue(6); // In
    tick();

    expect(comp.usesSet(0)).toBeTrue();          // → ws-select [multiple] path renders
    expect(comp.useCategoryPicker(0)).toBeTrue();
    expect(comp.categoryOptions().map(o => o.value)).toEqual(['Laptops', 'Servers', 'Calculators']);

    // The picker writes the CSV set the form submits; a stored set round-trips unchanged.
    comp.conditionsArray.at(0).get('valueSet')?.setValue('Laptops, Calculators');
    expect(comp.categoryValueUnknown(0)).toBeFalse();   // both are real categories
  }));

  // (c) A saved value that matches no category is shown with a warning, kept, and not rewritten.
  it('flags a saved category value that matches nothing, without deleting it', fakeAsync(() => {
    const typoRule = makeApiRule({
      trigger: {
        _schema: 1 as const,
        logicalOperator: 'And' as unknown as never,
        conditions: [
          { field: 'category', operator: 'Equal' as unknown as never, value: { type: 'String' as unknown as never, raw: 'Laptps', set: null } },
        ],
      },
    });
    const comp = setup(makePlan(typoRule), RULE_ID);
    comp.ngOnInit();
    tick();              // loadPromise → _loadExistingRule pushes the condition
    flushCatalogs();     // categories arrive → reconcile runs
    tick();

    expect(comp.categoryValueUnknown(0)).toBeTrue();     // warning shown
    expect(comp.valueRawAt(0)).toBe('Laptps');           // value preserved, never rewritten
    expect(comp.customValueAt(0)).toBeTrue();            // dropped to free text so it stays visible
  }));

  // (d) With no categories yet, the picker is off and the admin can still type a value (escape hatch).
  it('lets the admin type a value when there are no categories yet', fakeAsync(() => {
    const comp = setup(makePlan(makeApiRule()), null);
    comp.ngOnInit();
    tick();
    flushCatalogs([]);   // empty tenant / not synced

    comp.addCondition();
    comp.conditionsArray.at(0).get('field')?.setValue('category');
    tick();

    expect(comp.categoryListEmpty(0)).toBeTrue();
    expect(comp.useCategoryPicker(0)).toBeFalse();       // free text, not blocked
    comp.conditionsArray.at(0).get('valueRaw')?.setValue('Laptops');
    expect(comp.valueRawAt(0)).toBe('Laptops');
  }));
});

// The always-visible help under Table Type replaces the old hover tooltip that only really described
// Flat. Each type must surface its OWN explanation so the user sees the right one for what they picked.
describe('RuleFormComponent — rate table type help text', () => {
  let httpMock: HttpTestingController;

  function setup(): RuleFormComponent {
    const planSignal = signal<Plan | null>(makePlan(makeApiRule()));
    TestBed.configureTestingModule({
      imports: [RuleFormComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PlansStore,
          useValue: {
            selectedPlan: planSignal as unknown as PlansStore['selectedPlan'],
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
      set: { imports: [ReactiveFormsModule, TranslateModule], template: `<form [formGroup]="form"></form>` },
    });
    httpMock = TestBed.inject(HttpTestingController);
    const comp = TestBed.createComponent(RuleFormComponent).componentInstance;
    comp.ngOnInit();
    httpMock.expectOne('/api/plans/trigger-fields').flush([]);
    httpMock.expectOne('/api/plans/category-values').flush([]);
    return comp;
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('surfaces the help key matching the selected rate table type', fakeAsync(() => {
    const comp = setup();
    tick();

    comp.form.get('rateTable.type')!.setValue(RateTableType.Flat);
    expect(comp.rateTableHintKey()).toBe('PLANS.RATE_TABLE_HINT_FLAT');

    comp.form.get('rateTable.type')!.setValue(RateTableType.Tiered);
    expect(comp.rateTableHintKey()).toBe('PLANS.RATE_TABLE_HINT_TIERED');

    comp.form.get('rateTable.type')!.setValue(RateTableType.AttainmentBased);
    expect(comp.rateTableHintKey()).toBe('PLANS.RATE_TABLE_HINT_ATTAINMENT');
  }));
});
