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
