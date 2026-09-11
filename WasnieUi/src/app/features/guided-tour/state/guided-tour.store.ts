import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { WsGuideStep, WsGuideStepState } from '../../../shared/ui';
import {
  EMPTY_SANDBOX_STATUS, GuidedStepId, PromotedPlan, SandboxExperiment, SandboxStatus,
} from '../models/guided-tour.model';
import { SandboxApiService } from '../services/sandbox.api.service';
import { extractApiErrorCode } from '../../../shared/utils/api-error';
import {
  isKnownRateTableError, rateTableErrorKey, rateTableErrorParams,
} from '../../plans/rule-form/rate-table-error';

/** El orden del recorrido, que es el orden que impone el motor. */
export const GUIDED_STEPS: GuidedStepId[] = [
  'plan', 'rule', 'payee', 'quota', 'assignment',
  'transaction', 'calculate', 'payrun', 'approve', 'pay',
];

@Injectable({ providedIn: 'root' })
export class GuidedTourStore {
  private readonly api = inject(SandboxApiService);

  readonly status = signal<SandboxStatus>(EMPTY_SANDBOX_STATUS);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  /** Los valores que interpola la frase del error, cuando es un rechazo codificado de la escalera. */
  readonly errorParams = signal<Record<string, unknown> | null>(null);

  /** Lo último que hizo el usuario, para que la zona de guía pueda contarlo. */
  readonly lastDone = signal<GuidedStepId | null>(null);

  private readonly openStep = signal<GuidedStepId | null>(null);

  /**
   * Si un paso ya está hecho.
   *
   * ★★ SE DERIVA DEL DATO, SIEMPRE. No hay una marca de «paso completado»: hay un plan o no lo hay.
   * Una bandera de progreso se desincroniza en cuanto el usuario resetea o crea algo por otro camino,
   * y entonces el recorrido bloquea un paso que ya tiene sus piezas, o promete uno que no.
   */
  isDone(id: GuidedStepId): boolean {
    const s = this.status();
    switch (id) {
      case 'plan': return s.plan !== null;
      case 'rule': return (s.plan?.ruleCount ?? 0) > 0;
      case 'payee': return s.payee !== null;
      case 'quota': return s.quota !== null;
      case 'assignment': return s.assignment !== null;
      case 'transaction': return s.transaction !== null;
      case 'calculate': return s.credits.length > 0;
      case 'payrun': return s.payRun !== null;
      case 'approve': return s.payRun?.status === 'Approved' || s.payRun?.status === 'Paid';
      case 'pay': return s.payRun?.status === 'Paid';
    }
  }

  /**
   * Qué le falta a un paso para poder ejecutarse, o `null` si ya puede.
   *
   * ★ ES LA DEPENDENCIA DEL SISTEMA, NO UNA REGLA DEL RECORRIDO. Devuelve la clave del motivo, para
   * que la pantalla lo diga en el idioma del usuario en vez de mostrar «no disponible».
   */
  blockedReasonKey(id: GuidedStepId): string | null {
    const s = this.status();
    switch (id) {
      case 'plan': return null;
      case 'rule': return s.plan ? null : 'GUIDED.BLOCKED.NEEDS_PLAN';
      case 'payee': return null;
      case 'quota': return s.plan && s.payee ? null : 'GUIDED.BLOCKED.NEEDS_PLAN_AND_PAYEE';
      case 'assignment': return s.plan && s.payee ? null : 'GUIDED.BLOCKED.NEEDS_PLAN_AND_PAYEE';
      case 'transaction': return s.payee ? null : 'GUIDED.BLOCKED.NEEDS_PAYEE';
      case 'calculate':
        if (!s.transaction) return 'GUIDED.BLOCKED.NEEDS_TRANSACTION';
        return s.assignment && (s.plan?.ruleCount ?? 0) > 0
          ? null
          : 'GUIDED.BLOCKED.NEEDS_RULE_AND_ASSIGNMENT';
      case 'payrun': return s.credits.length > 0 ? null : 'GUIDED.BLOCKED.NEEDS_CREDITS';
      case 'approve': return s.payRun ? null : 'GUIDED.BLOCKED.NEEDS_PAYRUN';
      case 'pay':
        if (!s.payRun) return 'GUIDED.BLOCKED.NEEDS_PAYRUN';
        return s.payRun.status === 'Approved' || s.payRun.status === 'Paid'
          ? null
          : 'GUIDED.BLOCKED.NEEDS_APPROVAL';
    }
  }

  /** El primer paso que aún no está hecho: donde el recorrido continúa. */
  readonly nextStep = computed<GuidedStepId>(() => {
    this.status();
    return GUIDED_STEPS.find(id => !this.isDone(id)) ?? GUIDED_STEPS[GUIDED_STEPS.length - 1];
  });

  /**
   * El paso que se está mirando.
   *
   * ★ MIRAR NO ES EJECUTAR. Si el usuario no ha abierto ninguno, se muestra donde va el recorrido; si
   * abrió uno posterior, se respeta — leer adelante es cómo se entiende qué falta, y el botón de
   * ejecutar sigue apagado hasta que sus piezas existan.
   */
  readonly currentStep = computed<GuidedStepId>(() => this.openStep() ?? this.nextStep());

  readonly steps = computed<WsGuideStep[]>(() => {
    const current = this.currentStep();
    return GUIDED_STEPS.map(id => {
      const done = this.isDone(id);
      const blocked = this.blockedReasonKey(id) !== null;
      const state: WsGuideStepState =
        done ? 'done' : id === current ? 'current' : blocked ? 'blocked' : 'available';

      return {
        id,
        title: `GUIDED.STEP.${id.toUpperCase()}.TITLE`,
        description: `GUIDED.STEP.${id.toUpperCase()}.SHORT`,
        state,
        blockedReason: this.blockedReasonKey(id) ?? undefined,
      };
    });
  });

  readonly completed = computed(() => GUIDED_STEPS.filter(id => this.isDone(id)).length);

  open(id: string): void {
    this.openStep.set(id as GuidedStepId);
  }

  /** Vuelve a seguir el recorrido en vez de quedarse donde el usuario estaba mirando. */
  followProgress(): void {
    this.openStep.set(null);
  }

  async refresh(): Promise<void> {
    this.loading.set(true);
    try {
      this.status.set(await firstValueFrom(this.api.status()));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Ejecuta un paso y vuelve a leer el estado.
   *
   * ★★ EL ESTADO SE RELEE DEL SERVIDOR, NO SE ADIVINA DEL RESULTADO. Lo que el recorrido enseña tiene
   * que ser lo que el motor hizo: si calcular no generó créditos porque faltaba algo, el paso siguiente
   * tiene que seguir bloqueado — y sólo el servidor sabe eso. Deducirlo del «éxito» de la llamada sería
   * exactamente el verde que no prueba nada.
   */
  async run(action: () => Promise<unknown>, step: GuidedStepId): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.errorParams.set(null);
    try {
      await action();
      await this.refresh();
      this.lastDone.set(step);
      this.followProgress();
    } catch (err: unknown) {
      this.setError(err);
    } finally {
      this.busy.set(false);
    }
  }

  // ── Historial de experimentos ───────────────────────────────────────────────────────

  readonly experiments = signal<SandboxExperiment[]>([]);

  /** El experimento abierto para revisar, o `null` cuando se está trabajando en el recorrido en curso. */
  readonly openExperiment = signal<SandboxExperiment | null>(null);

  async loadExperiments(): Promise<void> {
    this.experiments.set(await firstValueFrom(this.api.listExperiments()));
  }

  /**
   * Guarda lo que hay ahora mismo como experimento.
   *
   * ★ LA FOTO ES EL ESTADO QUE LA PANTALLA YA TIENE. No se vuelve a consultar nada ni se recompone:
   * se guarda exactamente lo que el usuario está viendo, que es lo que va a querer reconocer cuando lo
   * abra dentro de un mes.
   */
  async saveExperiment(name: string, id: string | null = null): Promise<void> {
    const snapshot = JSON.stringify(this.status());
    await firstValueFrom(this.api.saveExperiment({ id, name, snapshot }));
    await this.loadExperiments();
  }

  async deleteExperiment(id: string): Promise<void> {
    await firstValueFrom(this.api.deleteExperiment(id));
    if (this.openExperiment()?.id === id) this.openExperiment.set(null);
    await this.loadExperiments();
  }

  /** Abre un experimento para mirarlo. No toca los datos del recorrido en curso. */
  review(experiment: SandboxExperiment | null): void {
    this.openExperiment.set(experiment);
  }

  /** El estado guardado de un experimento, listo para pintar con el mismo panel de resultados. */
  reviewedStatus(): SandboxStatus | null {
    const open = this.openExperiment();
    if (!open) return null;
    try {
      return JSON.parse(open.snapshot) as SandboxStatus;
    } catch {
      // Una foto ilegible no puede tumbar la pantalla: se comporta como un experimento vacío.
      return EMPTY_SANDBOX_STATUS;
    }
  }

  // ── Promoción a plan real ───────────────────────────────────────────────────────────

  readonly promoting = signal(false);

  /** El plan real que salió de la última promoción, para poder enseñarlo y enlazarlo. */
  readonly promoted = signal<PromotedPlan | null>(null);
  readonly promoteError = signal<string | null>(null);

  /**
   * Si hay algo que promover.
   *
   * ★ SE PIDE UNA REGLA, NO SÓLO UN PLAN. Un plan sin reglas no paga nada; promoverlo daría un plan
   * real vacío y la sensación de que el sandbox no sirvió para nada. El backend lo rechaza igual —
   * esto sólo evita ofrecer un botón que va a fallar.
   */
  readonly canPromote = computed(() => {
    const s = this.status();
    return s.plan !== null && (s.plan.ruleCount ?? 0) > 0;
  });

  /**
   * Crea el plan REAL con la configuración del recorrido.
   *
   * ★★ NO TOCA EL RECORRIDO, NI SIQUIERA AL SALIR BIEN. Promover es copiar: el experimento sigue
   * donde estaba y el usuario puede seguir probando, resetear o volver a promover. Recargar o limpiar
   * el sandbox aquí sería quitarle lo que acaba de descubrir.
   */
  async promote(): Promise<void> {
    if (this.promoting()) return;
    this.promoting.set(true);
    this.promoteError.set(null);
    try {
      this.promoted.set(await firstValueFrom(this.api.promote()));
    } catch (err: unknown) {
      this.promoteError.set(this.messageOf(err));
    } finally {
      this.promoting.set(false);
    }
  }

  async reset(): Promise<void> {
    await this.run(() => firstValueFrom(this.api.reset()), 'plan');
    this.lastDone.set(null);
    // El plan promovido sigue existiendo en producción; lo que se va es el aviso, que hablaba de un
    // recorrido que ya no está. Dejarlo en pantalla sugeriría que lo de abajo salió de lo de arriba.
    this.promoted.set(null);
    this.promoteError.set(null);
    this.followProgress();
  }

  /**
   * El error de un paso, dicho en el idioma del usuario.
   *
   * ★★ LOS RECHAZOS DE LA ESCALERA LLEGAN CODIFICADOS Y SIN `message`. Es el error más frecuente de
   * quien viene al sandbox a probar tramos — un hueco, un solape, una tasa fuera de rango — y leyendo
   * sólo `message` se veía «algo salió mal». Pasa por la MISMA lista blanca que el guardado de la
   * pantalla real, con sus parámetros, así que dice qué tramo y qué número. Un código que esta
   * versión no conoce cae al mensaje plano, nunca a un identificador interno en pantalla.
   */
  private setError(err: unknown): void {
    const coded = extractApiErrorCode(err);

    if (coded && isKnownRateTableError(coded)) {
      this.error.set(rateTableErrorKey(coded));
      this.errorParams.set(rateTableErrorParams(coded));
      return;
    }

    this.error.set(this.messageOf(err));
    this.errorParams.set(null);
  }

  /** El mensaje del backend si lo hay; si no, una clave genérica. Nunca un objeto crudo en pantalla. */
  private messageOf(err: unknown): string {
    const body = (err as { error?: { message?: string } } | null)?.error;
    return body?.message ?? 'ERRORS.GENERIC';
  }
}
