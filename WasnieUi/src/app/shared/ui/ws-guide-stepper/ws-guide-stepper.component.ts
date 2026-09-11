import { Component, computed, input, output } from '@angular/core';
import { IconComponent } from '../../components/icon/icon.component';

/**
 * En qué punto está un paso.
 *
 * ★★ `blocked` ES LA RAZÓN DE SER DE ESTE COMPONENTE, y lo que lo separa del indicador de progreso.
 * Un stepper corriente sólo sabe decir dónde estás; éste tiene que decir además por qué NO podés hacer
 * algo todavía — y esa dependencia no es una regla inventada por la pantalla, es la del sistema: no
 * hay assignment sin plan y payee, ni créditos sin transacción, ni pay run sin créditos.
 */
export type WsGuideStepState = 'done' | 'current' | 'available' | 'blocked';

export interface WsGuideStep {
  /** Identificador estable del paso; es lo que viaja en `stepSelect`. */
  id: string;
  /** Título corto, ya traducido por quien lo usa. */
  title: string;
  /** La explicación del paso. Es contenido, no adorno: es la mitad que enseña. */
  description?: string;
  state: WsGuideStepState;
  /**
   * Por qué está bloqueado, ya traducido. Sólo se pinta en estado `blocked`.
   *
   * ★ UN BLOQUEO SIN MOTIVO ES UN CALLEJÓN. «No podés» deja al usuario buscando qué hizo mal; «primero
   * necesitás una transacción calculada» le dice exactamente adónde ir.
   */
  blockedReason?: string;
}

/**
 * El panel de pasos guiado: lista vertical con título, descripción y estado por paso.
 *
 * ★★ ES UN COMPONENTE APARTE DE `WsWizard`, Y ESO SE DECIDIÓ A PROPÓSITO (KAN-68). El wizard
 * horizontal responde «dónde estoy en el progreso»: círculo, etiqueta corta, línea. Éste responde
 * «entendé qué hace cada paso»: descripción larga, motivo del bloqueo, lectura por adelantado. Meter
 * lo segundo dentro del primero habría dejado un componente con dos propósitos, que es como empiezan
 * los componentes que nadie se atreve a tocar.
 *
 * ★ MISMOS TOKENS DE ESTADO QUE EL WIZARD. Distintos en estructura, idénticos en apariencia: el
 * verde de «hecho» y el acento de «actual» salen de los mismos tokens, para que los dos se sientan
 * del mismo producto.
 *
 * ★ NO SABE NADA DEL ONBOARDING. Recibe pasos y emite cuál se eligió; quién decide los estados y qué
 * significa cada paso es de quien lo usa. Por eso vive en `shared/ui` y no en la feature.
 */
@Component({
  selector: 'ws-guide-stepper',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './ws-guide-stepper.component.html',
  styleUrl: './ws-guide-stepper.component.scss',
})
export class WsGuideStepperComponent {
  readonly steps = input.required<WsGuideStep[]>();

  /**
   * Si se puede pulsar un paso para leerlo.
   *
   * ★ LEER ADELANTE SÍ, EJECUTAR FUERA DE ORDEN NO. Este componente sólo navega la LECTURA; que un
   * paso se pueda ejecutar lo decide el estado, no el clic. Por eso hasta un paso bloqueado es
   * seleccionable: mirarlo es justamente cómo el usuario entiende qué le falta.
   */
  readonly navigable = input(true);

  readonly stepSelect = output<string>();

  /** El número que se pinta en el círculo: la posición, que es lo que el usuario cuenta. */
  readonly numbered = computed(() =>
    this.steps().map((step, index) => ({ ...step, position: index + 1 }))
  );

  select(step: WsGuideStep): void {
    if (this.navigable()) this.stepSelect.emit(step.id);
  }
}
