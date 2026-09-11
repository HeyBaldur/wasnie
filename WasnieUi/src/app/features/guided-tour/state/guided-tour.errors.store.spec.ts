import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { throwError } from 'rxjs';
import { GuidedTourStore } from './guided-tour.store';
import { SandboxApiService } from '../services/sandbox.api.service';

/**
 * El error de un paso del recorrido, dicho en el idioma del usuario.
 *
 * ★ LA REGLA DEL SANDBOX ES LA COMPLETA, y quien viene a probar tramos se topa con los rechazos de la
 * escalera — un hueco, un solape, una tasa fuera de rango. Llegan CODIFICADOS y sin `message`; leyendo
 * sólo `message` la pantalla decía «algo salió mal». Estas pruebas fijan que se traducen con la misma
 * lista blanca que el guardado de la pantalla real, con sus números.
 */
describe('GuidedTourStore · errores de un paso', () => {
  let store: GuidedTourStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        GuidedTourStore,
        { provide: SandboxApiService, useValue: jasmine.createSpyObj('SandboxApiService', ['status']) },
      ],
    });
    store = TestBed.inject(GuidedTourStore);
  });

  const failWith = (error: unknown) => store.run(() => Promise.reject(error), 'rule');

  it('traduce un rechazo codificado de la escalera, con sus parámetros y sin el `bound`', async () => {
    await failWith(new HttpErrorResponse({
      status: 422,
      error: {
        status: 422,
        code: 'RateTableTiersLeaveGap',
        parameters: { tierNumber: 1, nextTierNumber: 2, endsAt: 100, nextStartsAt: 120, bound: 'Amount' },
      },
    }));

    expect(store.error()).toBe('PLANS.RATE_TABLE_ERR_GAP_AMOUNT');
    expect(store.errorParams()).toEqual({ tierNumber: 1, nextTierNumber: 2, endsAt: 100, nextStartsAt: 120 });
  });

  it('un código que esta versión no conoce cae al mensaje, nunca al identificador', async () => {
    await failWith(new HttpErrorResponse({
      status: 422,
      error: { code: 'SomethingNew', message: 'Plain message.' },
    }));

    expect(store.error()).toBe('Plain message.');
    expect(store.errorParams()).toBeNull();
  });

  it('sin mensaje ni código, una clave genérica', async () => {
    await failWith(new HttpErrorResponse({ status: 500, error: null }));

    expect(store.error()).toBe('ERRORS.GENERIC');
  });
});
