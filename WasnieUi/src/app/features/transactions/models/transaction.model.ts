export enum TransactionStatus {
  Pending = 'Pending',
  Eligible = 'Eligible',
  Calculated = 'Calculated',
  Paid = 'Paid',
  Cancelled = 'Cancelled',
}

export enum TransactionSource {
  Manual = 'Manual',
}

export interface Transaction {
  id: string;
  tenantId: string;
  referenceNumber: string;
  /** Human-readable label of the sale (HubSpot deal name / manual / Excel). Display only. */
  description?: string | null;
  /** What was sold — `description` says which sale, these say which product. */
  productName?: string | null;
  productSku?: string | null;
  /** Enrichment output — the resolved category a rule trigger can filter on. Null when unmapped. */
  category?: string | null;
  payeeId: string | null;
  amount: number;
  currency: string;
  quantity: number;
  transactionDate: string;
  ingestedAt: string;
  source: TransactionSource;
  status: TransactionStatus;
  /** External system id (e.g. HubSpot deal-lineitem). Null for manual transactions. */
  externalId?: string | null;
  payeeName?: string | null;
  payeeEmployeeCode?: string | null;
  cancelledBy?: string | null;
  cancelledAt?: string | null;
  cancelledReason?: string | null;
  /** The admin's explicit plan attribution, when one was required at ingest. */
  selectedPlanAssignmentId?: string | null;
}

/**
 * One plan a transaction could be credited to. Identified by ASSIGNMENT, not plan: a payee can hold
 * two assignments to the same plan over different periods, and only the assignment is unambiguous.
 */
export interface PlanOption {
  planAssignmentId: string;
  planId: string;
  planName: string;
  planCurrency: string;
  effectiveStart: string;
  effectiveEnd: string;
}

export interface PlanOptions {
  options: PlanOption[];
  /** Server-computed (2+ options). The form must not decide this for itself. */
  selectionRequired: boolean;
}

export interface CreateTransactionRequest {
  referenceNumber: string;
  description?: string | null;
  productName?: string | null;
  productSku?: string | null;
  /** Optional. An explicitly chosen category wins over the SKU/name resolver on the server. */
  category?: string | null;
  payeeId: string | null;
  amount: number;
  currency: string;
  quantity: number;
  transactionDate: string;
  processImmediately?: boolean;
  /** Required when the payee has 2+ applicable plans; the server re-validates it. */
  selectedPlanAssignmentId?: string | null;
}

export interface AssignPayeeRequest {
  payeeId: string;
  comment?: string | null;
}

export interface ReassignPayeeRequest {
  newPayeeId: string;
  reason: string;
}

export interface VoidTransactionRequest {
  reason: string;
}
