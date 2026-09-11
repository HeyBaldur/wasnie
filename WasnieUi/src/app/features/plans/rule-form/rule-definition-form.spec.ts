import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Observable, of } from 'rxjs';
import { createRuleDefinitionForm, RuleDefinitionForm } from './rule-definition-form';
import {
  CapScope,
  MeasurementAggregation,
  MeasurementType,
  ModifierType,
  RateTableType,
  Rule,
  RuleSimulation,
  RuleSimulationBlocker,
  SimulateRuleRequest,
} from '../models/rule.model';

/**
 * The body a rule is saved with, shared by the Plans rule page and the guided tour's sandbox.
 *
 * ★★ WRITTEN AGAINST THE DEFECTS THAT MADE IT SHARED. The tour's hand-written rule sent its cap and
 * floor as `{ type, value }` and its attainment ladder as `{ from, to }`. The backend reads neither
 * shape, so both arrived as their defaults — a cap of zero, bounds of zero — and every request
 * succeeded. These pin the shape the server actually reads (`Cap.Amount` is Money, the attainment
 * tier properties are `AttainmentFrom/AttainmentTo`), so a second screen building its own payload
 * is no longer the only thing standing between a form and a silent zero.
 */
describe('RuleDefinitionForm — the definition the server receives', () => {
  let planId: string;
  let simulate: jasmine.Spy<(request: SimulateRuleRequest) => Observable<RuleSimulation>>;

  function make(currency = 'EUR'): RuleDefinitionForm {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return TestBed.runInInjectionContext(() => createRuleDefinitionForm({
      planId: () => planId,
      currency: signal(currency),
      readOnly: signal(false),
      simulate,
    }));
  }

  beforeEach(() => {
    planId = 'plan-1';
    simulate = jasmine.createSpy('simulate').and.returnValue(of({
      simulated: true,
      blocker: RuleSimulationBlocker.None,
      creditGenerated: true,
      commissionAmount: 50,
      currency: 'EUR',
      steps: [],
    }));
  });

  afterEach(() => TestBed.resetTestingModule());

  it('★ sends the cap and the floor as Money in the plan currency, with the scope by name', () => {
    const def = make('PLN');
    def.form.patchValue({ name: 'r', hasCap: true, cap: { amount: 500 }, hasFloor: true, floor: { amount: 100 } });

    const body = def.buildDefinition();

    expect(body.cap).toEqual({
      _schema: 1,
      amount: { amount: 500, currency: 'PLN' },
      scope: 'PerTransaction' as unknown as CapScope,
    });
    expect(body.floor).toEqual({ _schema: 1, amount: { amount: 100, currency: 'PLN' } });
  });

  it('sends no cap and no floor while their sections are switched off', () => {
    const def = make();
    def.form.patchValue({ name: 'r', cap: { amount: 500 }, floor: { amount: 100 } });

    const body = def.buildDefinition();

    expect(body.cap).toBeNull();
    expect(body.floor).toBeNull();
  });

  it('★ sends an attainment ladder under the names the engine reads, and does not rescale it', () => {
    // The tour divided these bounds by 100 and posted them as `from`/`to`. The unit is a proportion of
    // quota (1 = 100%), and it must reach the server exactly as typed.
    const def = make();
    def.form.patchValue({ name: 'r', rateTable: { type: RateTableType.AttainmentBased } });
    def.addAttainmentTier();
    def.addAttainmentTier();
    def.attainmentTiersArray.at(0).setValue({ attainmentFrom: 0, attainmentTo: 1, rate: 0.03 });
    def.attainmentTiersArray.at(1).setValue({ attainmentFrom: 1, attainmentTo: null, rate: 0.07 });

    const table = def.buildDefinition().rateTable;

    expect(table.type).toBe(RateTableType.AttainmentBased);
    expect(table.attainmentTiers).toEqual([
      { attainmentFrom: 0, attainmentTo: 1, rate: 0.03 },
      { attainmentFrom: 1, attainmentTo: null, rate: 0.07 },
    ]);
    expect(table.tiers).toBeNull();
    expect(table.flatRate).toBeNull();
  });

  it('never sends splitAtQuota on a Tiered table — it only means something against a quota', () => {
    const def = make();
    def.form.patchValue({ name: 'r', rateTable: { type: RateTableType.Tiered, splitAtQuota: true } });
    def.addTier();

    const table = def.buildDefinition().rateTable;

    expect(table.splitAtQuota).toBeFalse();
    expect(table.tiers).toEqual([{ from: 0, to: null, rate: 0.05 }]);
    expect(table.attainmentTiers).toBeNull();
  });

  it('reads the plan id when the body is built, not when the form was created', () => {
    // In the sandbox the plan is created two steps after the form exists.
    const def = make();
    def.form.patchValue({ name: 'r' });

    planId = 'plan-created-later';

    expect(def.buildDefinition().planId).toBe('plan-created-later');
  });

  it('keeps the modifier id of a loaded rule instead of minting a new one', () => {
    const def = make();
    def.patchFromRule({
      id: 'rule-1', name: 'r', sortOrder: 1, isActive: true, trigger: null, cap: null, floor: null,
      stoppedAt: null, stoppedBy: null, stopReason: null,
      measurement: { _schema: 1, type: MeasurementType.Revenue, sourceField: 'amount', aggregation: MeasurementAggregation.Sum },
      rateTable: { _schema: 1, type: RateTableType.Flat, flatRate: 0.05, tiers: null, attainmentTiers: null, splitAtQuota: false },
      modifier: { _schema: 1, id: 'mod-1', name: 'Boost', type: ModifierType.Multiplier, factor: 1.2, trigger: null },
    } as Rule);

    expect(def.buildDefinition().modifier?.id).toBe('mod-1');
  });

  it('★ simulates through the host, so the sandbox asks about the sandbox plan', fakeAsync(() => {
    const def = make();
    def.form.patchValue({ name: 'r', rateTable: { flatRate: 0.05 } });

    def.onSimInput(1000);
    tick(300);

    expect(simulate).toHaveBeenCalledTimes(1);
    const request = simulate.calls.mostRecent().args[0];
    expect(request.planId).toBe('plan-1');
    expect(request.amount).toBe(1000);
    expect(def.simulation()?.commissionAmount).toBe(50);
  }));
});
