import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { GuidedTourStore } from './guided-tour.store';
import { SandboxApiService } from '../services/sandbox.api.service';
import {
  configFingerprint, EMPTY_SANDBOX_STATUS, parseExperiment, SandboxExperiment, SandboxExperimentConfig, SandboxStatus,
} from '../models/guided-tour.model';

/**
 * El historial de experimentos: lo que se guarda y lo que se puede volver a cargar.
 *
 * ★★ DOS COSAS QUE ESTAS PRUEBAS FIJAN. Renombrar ya no sobreescribe el experimento con lo que haya en
 * pantalla (antes lo hacía), y un experimento guarda la configuración completa además de la foto, sin
 * dejar de leer los experimentos viejos que sólo tenían la foto.
 */
describe('GuidedTourStore · experimentos', () => {
  const status = (): SandboxStatus => ({
    ...EMPTY_SANDBOX_STATUS,
    plan: {
      id: 'p-1', name: 'Plan de ventas', currency: 'EUR',
      effectiveStart: '2026-01-01', effectiveEnd: '2026-12-31', ruleCount: 1,
    },
  });

  const config: SandboxExperimentConfig = {
    plan: { name: 'Plan de ventas', description: '', currency: 'EUR', periodStart: '2026-01-01', periodEnd: '2026-12-31' },
    rules: [],
    payee: { fullName: 'Alex Demo', employeeCode: 'DEMO-001', email: '', role: '', employmentType: '', location: '' },
    quota: { amount: 100000, measurement: 0 },
    transaction: { reference: 'DEMO-0001', amount: 10000, date: '2026-05-01', description: '' },
  };

  const saved = (over: Partial<SandboxExperiment> = {}): SandboxExperiment => ({
    id: 'e-1', name: 'Prueba', snapshot: '{}', createdAt: '2026-09-11T10:00:00Z', updatedAt: '2026-09-11T10:00:00Z',
    ...over,
  });

  let api: jasmine.SpyObj<SandboxApiService>;
  let store: GuidedTourStore;

  beforeEach(() => {
    api = jasmine.createSpyObj<SandboxApiService>('SandboxApiService', ['saveExperiment', 'listExperiments']);
    api.saveExperiment.and.callFake(body => of(saved({ id: body.id ?? 'e-new', name: body.name, snapshot: body.snapshot })));
    api.listExperiments.and.returnValue(of([]));

    TestBed.configureTestingModule({
      providers: [GuidedTourStore, { provide: SandboxApiService, useValue: api }],
    });
    store = TestBed.inject(GuidedTourStore);
  });

  it('★★ renombrar conserva la foto que tenía el experimento, no la del sandbox actual', async () => {
    const original = saved({ snapshot: '{"version":2,"status":{"plan":null},"config":null}' });
    store.status.set(status()); // lo que hay ahora en pantalla, que NO debe viajar

    await store.renameExperiment(original, 'Nombre nuevo');

    const body = api.saveExperiment.calls.mostRecent().args[0];
    expect(body.id).toBe('e-1');
    expect(body.name).toBe('Nombre nuevo');
    expect(body.snapshot).toBe(original.snapshot);
  });

  it('★ renombrar también renombra el plan de la configuración, y no toca resultados ni promoción', async () => {
    // Renombrar y después «Editar» cargaba el plan con el nombre viejo.
    const promotion = { planId: 'real-1', planName: 'Plan de ventas', fingerprint: 'f', promotedAt: '2026-09-11T10:00:00Z' };
    const original = saved({
      snapshot: JSON.stringify({ version: 2, status: status(), config, promotion }),
    });

    await store.renameExperiment(original, 'Plan Q4');

    const parsed = parseExperiment(api.saveExperiment.calls.mostRecent().args[0].snapshot);
    expect(parsed.config?.plan.name).toBe('Plan Q4');
    expect(parsed.config?.payee).toEqual(config.payee);
    expect(parsed.status.plan?.name).toBe('Plan de ventas');
    expect(parsed.promotion).toEqual(promotion);
  });

  it('guarda la foto Y la configuración, y las dos se vuelven a leer', async () => {
    store.status.set(status());

    const result = await store.saveExperiment('Prueba', null, config);

    const parsed = parseExperiment(result.snapshot);
    expect(parsed.status.plan?.name).toBe('Plan de ventas');
    expect(parsed.config).toEqual(config);
  });

  it('un experimento viejo (sólo la foto) se sigue leyendo, sin configuración', () => {
    const legacy = JSON.stringify(status());

    const parsed = parseExperiment(legacy);

    expect(parsed.status.plan?.name).toBe('Plan de ventas');
    expect(parsed.config).toBeNull();
  });

  describe('★★ huella de promoción (lo que impide los planes duplicados)', () => {
    const rule = (over: Record<string, unknown> = {}) => ({
      id: 'r-1', planId: 'p-1', name: 'Flat', sortOrder: 1, isActive: true,
      stoppedAt: null, stoppedBy: null, stopReason: null, trigger: null, cap: null, floor: null,
      modifier: { _schema: 1, id: 'm-1', name: 'Boost', type: 0, factor: 1.2, trigger: null },
      measurement: { _schema: 1, type: 0, sourceField: 'amount', aggregation: 0 },
      rateTable: { _schema: 1, type: 0, flatRate: 0.05, tiers: null, attainmentTiers: null, splitAtQuota: false },
      ...over,
    }) as unknown as SandboxExperimentConfig['rules'][number];

    it('la misma configuración recreada (ids nuevos del servidor) tiene la misma huella', () => {
      const a = { ...config, rules: [rule()] };
      const b = {
        ...config,
        rules: [rule({
          id: 'r-2', planId: 'p-2',
          modifier: { _schema: 1, id: 'm-2', name: 'Boost', type: 0, factor: 1.2, trigger: null },
        })],
      };

      expect(configFingerprint(a)).toBe(configFingerprint(b));
    });

    it('cambiar una tasa, el nombre del plan o una regla cambia la huella', () => {
      const base = configFingerprint({ ...config, rules: [rule()] });

      const otherRate = rule({ rateTable: { _schema: 1, type: 0, flatRate: 0.06, tiers: null, attainmentTiers: null, splitAtQuota: false } });
      expect(configFingerprint({ ...config, rules: [otherRate] })).not.toBe(base);
      expect(configFingerprint({ ...config, plan: { ...config.plan, name: 'Otro' }, rules: [rule()] })).not.toBe(base);
      expect(configFingerprint({ ...config, rules: [rule(), rule({ name: 'Segunda', sortOrder: 2 })] })).not.toBe(base);
    });

    it('lo que no cruza a producción (payee, cuota, venta) no cambia la huella', () => {
      const base = configFingerprint({ ...config, rules: [rule()] });
      const other = { ...config, rules: [rule()], quota: { amount: 5, measurement: 1 }, transaction: { ...config.transaction, amount: 1 } };

      expect(configFingerprint(other)).toBe(base);
    });

    it('la promoción se guarda con el experimento y se vuelve a leer', async () => {
      store.status.set(status());
      const promotion = { planId: 'real-1', planName: 'Plan de ventas', fingerprint: 'f', promotedAt: '2026-09-11T10:00:00Z' };

      const result = await store.saveExperiment('Prueba', null, config, promotion);

      expect(parseExperiment(result.snapshot).promotion).toEqual(promotion);
    });
  });

  it('una foto ilegible se comporta como un experimento vacío', () => {
    const parsed = parseExperiment('{not json');

    expect(parsed.status).toEqual(EMPTY_SANDBOX_STATUS);
    expect(parsed.config).toBeNull();
  });
});
