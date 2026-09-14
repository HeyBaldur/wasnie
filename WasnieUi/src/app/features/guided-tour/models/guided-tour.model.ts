import { Rule } from '../../plans/models/rule.model';

/**
 * Los diez pasos del ciclo, en el orden en que el sistema los exige.
 *
 * ★★ EL ORDEN NO ES PEDAGÓGICO, ES LA DEPENDENCIA REAL. No se puede asignar un plan sin plan y sin
 * payee, ni calcular una comisión sin una venta, ni pagar un pay run que no existe. El recorrido no
 * inventa una secuencia: refleja la que el motor ya impone, y por eso enseñarla enseña el producto.
 */
export type GuidedStepId =
  | 'plan'
  | 'rule'
  | 'payee'
  | 'quota'
  | 'assignment'
  | 'transaction'
  | 'calculate'
  | 'payrun'
  | 'approve'
  | 'pay';

export interface SandboxPlan {
  id: string;
  name: string;
  currency: string;
  effectiveStart: string;
  effectiveEnd: string;
  ruleCount: number;
}

/**
 * La regla que se acaba de crear, con lo suficiente para contar QUÉ se configuró.
 *
 * ★ LOS CAMPOS DEL RESUMEN SON OPCIONALES. Los experimentos guardados antes de que existieran llevan
 * una foto sin ellos; leerlos tiene que seguir funcionando, y lo que falta simplemente no se pinta.
 */
export interface SandboxRule {
  id: string;
  name: string;
  /** El nombre del enum tal cual lo manda el servidor («Flat», «Tiered», «AttainmentBased»). */
  tableType: string;
  /** La tasa plana tal como está guardada: un multiplicador (0.05), o importe por unidad en Units. */
  rate: number | null;
  measurementType?: string;
  tierCount?: number;
  conditionCount?: number;
  modifierFactor?: number | null;
  capAmount?: number | null;
  floorAmount?: number | null;
}

export interface SandboxPayee { id: string; fullName: string; employeeCode: string; }
export interface SandboxQuota { id: string; amount: number; currency: string; measurementType: string; }
export interface SandboxAssignment { id: string; effectiveStart: string; effectiveEnd: string; }

export interface SandboxTransaction {
  id: string;
  referenceNumber: string;
  amount: number;
  currency: string;
  transactionDate: string;
  status: string;
}

/**
 * Un crédito, con las dos mitades de la cuenta.
 *
 * ★ `baseAmount` Y `creditedAmount` VIAJAN JUNTOS PARA PODER ENSEÑAR LA OPERACIÓN. «10.000 × 5 % =
 * 500» es lo que el usuario vino a ver; un importe suelto no explica de dónde salió.
 */
export interface SandboxCredit {
  id: string;
  payeeName: string;
  ruleName: string;
  baseAmount: number;
  creditedAmount: number;
  currency: string;
  consumed: boolean;
  /**
   * Por qué la tabla de tasas se negó a tasar la venta (código del motor), o null si la tasó. Opcional:
   * los experimentos guardados antes no lo traen.
   */
  rateRefusal?: string | null;
}

export interface SandboxPayout {
  id: string;
  payeeName: string;
  total: number;
  currency: string;
  status: string;
  lineCount: number;
}

export interface SandboxPayRun {
  id: string;
  periodStart: string;
  periodEnd: string;
  status: string;
  payoutCount: number;
}

/**
 * Una venta calculada que no generó comisión, con el motivo.
 *
 * ★ `reason` ES UN CÓDIGO (NoPayee, NoActiveAssignment, CurrencyMismatch, NoApplicableRules,
 * TriggerNotMatched, NotCredited), NUNCA UNA FRASE. La pantalla lo traduce con una lista blanca.
 */
export interface SandboxUncreditedSale {
  referenceNumber: string;
  reason: string;
  ruleNames: string[];
}

/** Lo que devolvió el paso «calcular»: créditos creados y ventas que se quedaron sin comisión. */
export interface SandboxCalculation {
  creditsCreated: number;
  uncredited: SandboxUncreditedSale[];
}

/** Lo que existe ya en el recorrido de esta empresa. Llega del endpoint de estado. */
export interface SandboxStatus {
  plan: SandboxPlan | null;
  rule: SandboxRule | null;
  payee: SandboxPayee | null;
  quota: SandboxQuota | null;
  assignment: SandboxAssignment | null;
  transaction: SandboxTransaction | null;
  credits: SandboxCredit[];
  payRun: SandboxPayRun | null;
  payouts: SandboxPayout[];
}

/** El estado vacío, para pintar antes de la primera respuesta sin que nada quede indefinido. */
export const EMPTY_SANDBOX_STATUS: SandboxStatus = {
  plan: null,
  rule: null,
  payee: null,
  quota: null,
  assignment: null,
  transaction: null,
  credits: [],
  payRun: null,
  payouts: [],
};

/**
 * Lo que se introdujo en el recorrido: con esto se vuelven a llenar todos los formularios.
 *
 * ★★ LAS REGLAS VAN COMPLETAS, COMO LAS DEVUELVE PLANES. La foto de resultados sólo resume la regla
 * (tipo, tasa, cuántos tramos), y con un resumen no se puede volver a editar nada. Aquí va la definición
 * entera — tramos, disparador, modificador, tope, mínimo — leída del servidor con la misma consulta que
 * usa la pantalla real de reglas, en la misma forma que su formulario sabe cargar.
 */
export interface SandboxExperimentConfig {
  plan: { name: string; description: string; currency: string; periodStart: string; periodEnd: string };
  rules: Rule[];
  payee: {
    fullName: string; employeeCode: string; email: string; role: string;
    employmentType: string; location: string;
  };
  quota: { amount: number; measurement: number };
  transaction: { reference: string; amount: number; date: string; description: string };
}

/**
 * Lee un experimento guardado: su foto de resultados y, si la tiene, su configuración.
 *
 * ★ LOS VIEJOS SIGUEN LEYÉNDOSE. Antes se guardaba sólo la foto (el estado tal cual); ahora un documento
 * `{ version: 2, status, config }`. Un experimento viejo se enseña igual, sólo que sin configuración: se
 * puede revisar, no cargar. Una foto ilegible no tumba la pantalla: se comporta como un experimento vacío.
 */
export function parseExperiment(snapshot: string): {
  status: SandboxStatus;
  config: SandboxExperimentConfig | null;
  promotion: SandboxPromotionRecord | null;
} {
  try {
    const parsed = JSON.parse(snapshot);
    if (parsed && parsed.version === 2) {
      return {
        status: parsed.status ?? EMPTY_SANDBOX_STATUS,
        config: parsed.config ?? null,
        promotion: parsed.promotion ?? null,
      };
    }
    return { status: (parsed ?? EMPTY_SANDBOX_STATUS) as SandboxStatus, config: null, promotion: null };
  } catch {
    return { status: EMPTY_SANDBOX_STATUS, config: null, promotion: null };
  }
}

/** El documento que se guarda: la foto de resultados, la configuración que la produjo y, si la hubo, su promoción. */
export function serializeExperiment(
  status: SandboxStatus,
  config: SandboxExperimentConfig | null,
  promotion: SandboxPromotionRecord | null = null,
): string {
  return JSON.stringify({ version: 2, status, config, promotion });
}

/**
 * Qué configuración se llevó a producción, y como qué plan real.
 *
 * ★★ ES LO QUE IMPIDE LOS DUPLICADOS. Sin este registro, promover la misma prueba dos veces crea dos
 * planes reales idénticos — y un usuario llegó a crear diez. Con él, antes de promover se busca la misma
 * huella: si ya se promovió y ese plan real sigue existiendo, no se vuelve a crear.
 */
export interface SandboxPromotionRecord {
  planId: string;
  planName: string;
  fingerprint: string;
  promotedAt: string;
}

/**
 * Los campos que NO son configuración: los genera el servidor cada vez que se crea algo. Si entraran en
 * la huella, la misma regla recreada en el sandbox (ids nuevos) contaría como un cambio.
 */
const VOLATILE_KEYS = new Set(['id', 'planId', 'stoppedAt', 'stoppedBy', 'stopReason', 'isActive', 'createdAt', 'updatedAt']);

function canonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonical);
  if (value !== null && typeof value === 'object') {
    const source = value as Record<string, unknown>;
    return Object.keys(source)
      .filter(key => !VOLATILE_KEYS.has(key))
      .sort()
      .reduce<Record<string, unknown>>((out, key) => {
        out[key] = canonical(source[key]);
        return out;
      }, {});
  }
  return value;
}

/**
 * La huella de lo que cruza a producción: el plan y sus reglas completas. El payee, la cuota y la venta
 * no cruzan, así que cambiarlos no hace distinta la promoción. Cualquier otro cambio —el nombre, una
 * tasa, un tramo, una condición— sí, y entonces se puede volver a promover.
 */
export function configFingerprint(config: SandboxExperimentConfig): string {
  const rules = [...config.rules].sort((a, b) => a.sortOrder - b.sortOrder);
  return JSON.stringify(canonical({ plan: config.plan, rules }));
}

/**
 * Un experimento guardado: la configuración que alguien probó, con su nombre y su fecha.
 *
 * ★ `snapshot` llega como texto y se interpreta al abrirlo. Lo que se guarda es lo que la pantalla
 * enseñó, y esa forma cambia con los pasos; un contrato rígido obligaría a migrar el historial cada
 * vez que el recorrido gane un campo.
 */
export interface SandboxExperiment {
  id: string;
  name: string;
  snapshot: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * El plan REAL que nació de un experimento.
 *
 * ★ VIAJAN LOS DOS NOMBRES A PROPÓSITO. Si el del sandbox ya estaba ocupado en producción, el plan
 * queda con un sufijo; la pantalla tiene que poder decirlo, o el usuario buscará en la lista de
 * planes un nombre que no está.
 */
export interface PromotedPlan {
  planId: string;
  name: string;
  originalName: string;
  ruleCount: number;
}
