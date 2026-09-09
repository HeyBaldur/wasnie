import { Injectable } from '@angular/core';

/** Dónde aterriza toda salida de sesión. */
const LOGIN_URL = '/auth/login';

/**
 * El aviso de sesión caducada, guardado para el otro lado de la recarga.
 *
 * ★ EN `sessionStorage`, Y DE UN SOLO USO. Sobrevive a la recarga, muere con la pestaña y lo borra
 * quien lo lee. Un aviso que se quedara pegado reaparecería en el siguiente inicio de sesión
 * diciendo que caducó algo que no caducó.
 */
export const SESSION_EXPIRED_NOTICE_KEY = 'wasnie:session-expired-notice';

/**
 * Terminar la sesión terminando la aplicación.
 *
 * ★★ ESTO EXISTE PORQUE VACIAR EL TOKEN NO VACÍA LA APLICACIÓN, y ahí estaba la fuga. Al cerrar
 * sesión sólo se limpiaban el token y el usuario actual; la instancia de Angular seguía viva, y con
 * ella la VEINTENA de servicios `providedIn: 'root'` que guardan datos del tenant en memoria: los
 * contadores del sidebar, el estado de HubSpot, el plan contratado, las conversaciones del asistente,
 * los listados cacheados. Al entrar con OTRO tenant, esa memoria seguía ahí: el badge de Financials
 * mostraba los 41 del tenant anterior, el sidebar decía "conectado a HubSpot" y el asistente no
 * cargaba las conversaciones nuevas porque su lista, la del tenant anterior, no estaba vacía.
 *
 * ★★ POR QUÉ UNA RECARGA Y NO UN `reset()` EN CADA STORE. Un registro de reinicios hay que
 * mantenerlo: cada store nuevo, y cada campo nuevo dentro de un store, es una ocasión de olvidarse, y
 * el olvido no se nota hasta que alguien ve datos de otra empresa. Es la misma familia de defecto que
 * la bandera que se desincroniza (§B5). Cargar el documento de nuevo no se puede olvidar: no queda
 * memoria que reiniciar, ni peticiones en vuelo, ni temporizadores, ni suscripciones. En software que
 * calcula sueldos de varias empresas, la garantía vale mucho más que el segundo que cuesta.
 *
 * ★ Y ES UN SERVICIO, NO UN `window.location` SUELTO. Cada llamante lo tiene inyectado, así que en
 * los tests se sustituye por un espía; una llamada directa recargaría el propio ejecutor de tests.
 */
@Injectable({ providedIn: 'root' })
export class SessionExitService {
  /**
   * Deja la aplicación en la pantalla de acceso, con el documento cargado de cero.
   *
   * @param notice `'expired'` cuando el sistema terminó la sesión (inactividad, refresco fallido) y
   * hay que decírselo al usuario del otro lado. La pantalla de acceso lo muestra y lo borra.
   */
  toLogin(notice: 'expired' | null = null): void {
    if (notice === 'expired') {
      try {
        sessionStorage.setItem(SESSION_EXPIRED_NOTICE_KEY, '1');
      } catch {
        // Sin almacenamiento se pierde el aviso, nunca la salida.
      }
    }

    window.location.assign(LOGIN_URL);
  }
}
