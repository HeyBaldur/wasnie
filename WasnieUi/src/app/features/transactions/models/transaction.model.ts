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
  payeeId: string | null;
  amount: number;
  currency: string;
  quantity: number;
  transactionDate: string;
  ingestedAt: string;
  source: TransactionSource;
  status: TransactionStatus;
  payeeName?: string | null;
  payeeEmployeeCode?: string | null;
  cancelledBy?: string | null;
  cancelledAt?: string | null;
  cancelledReason?: string | null;
}

export interface CreateTransactionRequest {
  referenceNumber: string;
  description?: string | null;
  payeeId: string | null;
  amount: number;
  currency: string;
  quantity: number;
  transactionDate: string;
  processImmediately?: boolean;
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
