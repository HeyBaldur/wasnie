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
  payeeId: string;
  amount: number;
  currency: string;
  transactionDate: string;
  source: TransactionSource;
  status: TransactionStatus;
  payeeName?: string | null;
  payeeEmployeeCode?: string | null;
}

export interface CreateTransactionRequest {
  referenceNumber: string;
  payeeId: string;
  amount: number;
  currency: string;
  transactionDate: string;
}
