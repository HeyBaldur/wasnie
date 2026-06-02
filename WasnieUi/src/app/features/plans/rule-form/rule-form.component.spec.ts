import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
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
  };
}

// ---------------------------------------------------------------------------
// Test suite
// ---------------------------------------------------------------------------

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
});
