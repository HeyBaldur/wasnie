import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { GuidedTourStore } from './guided-tour.store';
import { SandboxApiService } from '../services/sandbox.api.service';
import { EMPTY_SANDBOX_STATUS, PromotedPlan, SandboxStatus } from '../models/guided-tour.model';

/**
 * La salida del sandbox: promover la configuración probada a un plan real (KAN-68, tanda 5).
 *
 * Lo que estas pruebas sostienen: que no se ofrezca promover lo que no paga nada, que un fallo del
 * servidor se cuente en vez de dejar la pantalla como si hubiera ido bien, y que promover NO toque el
 * recorrido — es una copia, y el usuario sigue teniendo su experimento donde estaba.
 */
describe('GuidedTourStore · promoción a plan real', () => {
  const status = (over: Partial<SandboxStatus> = {}): SandboxStatus => ({
    ...EMPTY_SANDBOX_STATUS,
    plan: {
      id: 'p-1', name: 'Plan de ventas', currency: 'EUR',
      effectiveStart: '2026-01-01', effectiveEnd: '2026-12-31', ruleCount: 1,
    },
    ...over,
  });

  const promoted: PromotedPlan = {
    planId: 'real-1', name: 'Plan de ventas (2)', originalName: 'Plan de ventas', ruleCount: 1,
  };

  let api: jasmine.SpyObj<SandboxApiService>;
  let store: GuidedTourStore;

  beforeEach(() => {
    api = jasmine.createSpyObj<SandboxApiService>('SandboxApiService', ['promote', 'status', 'reset']);
    api.promote.and.returnValue(of(promoted));
    api.status.and.returnValue(of(status()));

    TestBed.configureTestingModule({
      providers: [GuidedTourStore, { provide: SandboxApiService, useValue: api }],
    });

    store = TestBed.inject(GuidedTourStore);
  });

  it('no ofrece promover un plan sin reglas: un plan real vacío no paga nada', () => {
    store.status.set(status({ plan: { ...status().plan!, ruleCount: 0 } }));

    expect(store.canPromote()).toBeFalse();
  });

  it('ofrece promover en cuanto hay plan y regla', () => {
    store.status.set(status());

    expect(store.canPromote()).toBeTrue();
  });

  it('guarda el plan real que salió, con los dos nombres', async () => {
    await store.promote();

    expect(store.promoted()).toEqual(promoted);
    expect(store.promoteError()).toBeNull();
  });

  /**
   * ★ EL RECORRIDO NO SE TOCA. Promover copia; si además recargara o limpiara el sandbox, el usuario
   * perdería de vista el experimento justo cuando acaba de decidir que le sirve.
   */
  it('no vuelve a leer ni altera el estado del recorrido al promover', async () => {
    const before = store.status();

    await store.promote();

    expect(api.status).not.toHaveBeenCalled();
    expect(store.status()).toBe(before);
  });

  it('cuenta el fallo del servidor en vez de aparentar que salió bien', async () => {
    api.promote.and.returnValue(throwError(() => ({ error: { message: 'Plan Limit Reached' } })));

    await store.promote();

    expect(store.promoted()).toBeNull();
    expect(store.promoteError()).toBe('Plan Limit Reached');
  });

  it('empezar de cero retira el aviso, que hablaba de un recorrido que ya no está', async () => {
    api.reset.and.returnValue(of({ deleted: 4 }));
    await store.promote();

    await store.reset();

    expect(store.promoted()).toBeNull();
  });
});
