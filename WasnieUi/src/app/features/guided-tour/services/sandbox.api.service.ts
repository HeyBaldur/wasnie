import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PromotedPlan, SandboxExperiment, SandboxStatus } from '../models/guided-tour.model';
import { AddRuleRequest, RuleSimulation, SimulateRuleRequest } from '../../plans/models/rule.model';

/**
 * El ciclo completo, contra el sandbox.
 *
 * ★★ CADA MÉTODO ES LA MISMA OPERACIÓN QUE LA PANTALLA REAL, y lo único que cambia es la ruta. Detrás
 * de `api/sandbox` corren exactamente los comandos del producto — crear un plan es el mismo comando
 * que en Plans — escribiendo en un juego de tablas aparte. Por eso este servicio no valida ni calcula
 * nada: si lo hiciera, el día que cambie el motor el recorrido enseñaría el motor viejo.
 *
 * ★ LOS CUERPOS SON LOS DE LOS COMANDOS REALES, no un formato propio del recorrido. Cuando un comando
 * gane un campo, aquí se ve enseguida: falla la compilación o el servidor lo rechaza. Un DTO
 * intermedio «amable» sería justo la capa que envejece en silencio.
 */
@Injectable({ providedIn: 'root' })
export class SandboxApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/sandbox';

  status(): Observable<SandboxStatus> {
    return this.http.get<SandboxStatus>(`${this.base}/status`);
  }

  createPlan(body: {
    name: string;
    description: string;
    effectiveStart: string;
    effectiveEnd: string;
    currency: string;
  }): Observable<unknown> {
    return this.http.post(`${this.base}/plans`, body);
  }

  /**
   * ★★ EL CUERPO ES `AddRuleRequest`, EL TIPO DE LA PANTALLA REAL, Y NO UN `unknown`. Con `unknown`
   * el recorrido mandó el tope y el mínimo como `{ type, value }` y los tramos por cumplimiento como
   * `{ from, to }`: el servidor no los leía, ponía sus valores por defecto y nada fallaba. Tipado,
   * un cuerpo con otra forma no compila.
   */
  addRule(body: AddRuleRequest): Observable<unknown> {
    return this.http.post(`${this.base}/plans/rules`, body);
  }

  /** El simulador de la pantalla real de reglas, contra el plan de práctica. */
  simulateRule(planId: string, request: SimulateRuleRequest): Observable<RuleSimulation> {
    return this.http.post<RuleSimulation>(`${this.base}/plans/${planId}/rules/simulate`, request);
  }

  createPayee(body: {
    fullName: string;
    employeeCode: string;
    email: string | null;
    hireDate: string | null;
    role: string | null;
  }): Observable<unknown> {
    return this.http.post(`${this.base}/payees`, body);
  }

  createQuota(body: {
    payeeId: string;
    planId: string;
    measurementType: number;
    amount: number;
    currency: string;
    periodStart: string;
    periodEnd: string;
  }): Observable<unknown> {
    return this.http.post(`${this.base}/quotas`, body);
  }

  assign(body: {
    planId: string;
    payeeId: string;
    effectiveStart: string;
    effectiveEnd: string;
  }): Observable<unknown> {
    return this.http.post(`${this.base}/assignments`, body);
  }

  ingestTransaction(body: {
    referenceNumber: string;
    payeeId: string;
    amount: number;
    currency: string;
    transactionDate: string;
    description: string | null;
    processImmediately: boolean;
  }): Observable<unknown> {
    return this.http.post(`${this.base}/transactions`, body);
  }

  /**
   * Calcula las ventas pendientes, en el acto.
   *
   * ★ SIN CUERPO A PROPÓSITO. El camino normal del producto encola un trabajo en segundo plano y
   * recibe su ámbito por parámetros; ese trabajo corre con el contexto real, así que el recorrido llama
   * al mismo motor de asignación de forma síncrona sobre lo que hay pendiente en su propio juego de
   * tablas. Ver el comando en el backend.
   */
  process(): Observable<unknown> {
    return this.http.post(`${this.base}/transactions/process`, {});
  }

  calculatePayRun(body: { periodStart: string; periodEnd: string }): Observable<unknown> {
    return this.http.post(`${this.base}/pay-runs`, body);
  }

  approvePayRun(body: { payRunId: string }): Observable<unknown> {
    return this.http.post(`${this.base}/pay-runs/approve`, body);
  }

  markPaid(body: { payRunId: string }): Observable<unknown> {
    return this.http.post(`${this.base}/pay-runs/pay`, body);
  }

  // ── Historial de experimentos ───────────────────────────────────────────────────────────────

  listExperiments(): Observable<SandboxExperiment[]> {
    return this.http.get<SandboxExperiment[]>(`${this.base}/experiments`);
  }

  /**
   * Guarda el recorrido actual como experimento, o vuelve a guardar sobre uno existente.
   *
   * ★ SE GUARDA UNA FOTO, NO UN PUNTERO A LAS FILAS. Los datos del recorrido son de un solo juego —
   * resetear los borra — así que un experimento tiene que llevarse consigo lo que configuró y lo que
   * le dio; si sólo apuntara a las filas, el historial quedaría lleno de entradas vacías al primer
   * reseteo.
   */
  saveExperiment(body: { id: string | null; name: string; snapshot: string }): Observable<SandboxExperiment> {
    return this.http.post<SandboxExperiment>(`${this.base}/experiments`, body);
  }

  deleteExperiment(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/experiments/${id}`);
  }

  /**
   * Lleva la configuración probada a un plan REAL, en borrador.
   *
   * ★★ NO CUELGA DE `this.base`, Y NO ES UN DESCUIDO. Todo lo que hay detrás de `api/sandbox` escribe
   * en el esquema de práctica por construcción — un filtro marca la ruta entera. Ésta es la única
   * llamada del recorrido que escribe en las tablas del dinero real, así que tiene que salir de esa
   * ruta: si colgara de ella, «promover» crearía el plan otra vez en el sandbox y el usuario no
   * encontraría su plan real por ninguna parte.
   */
  promote(): Observable<PromotedPlan> {
    return this.http.post<PromotedPlan>('/api/plan-promotions', {});
  }

  /** Vacía todo lo creado practicando. Ver la nota del endpoint: no alcanza nada real. */
  reset(): Observable<{ deleted: number }> {
    return this.http.post<{ deleted: number }>(`${this.base}/reset`, {});
  }
}
