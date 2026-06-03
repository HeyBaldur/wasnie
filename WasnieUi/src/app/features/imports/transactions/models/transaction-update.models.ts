export interface TransactionUpdateColumnMapping {
  referenceNumberColumn: string;
  amountColumn?: string | null;
  currencyColumn?: string | null;
  transactionDateColumn?: string | null;
  payeeCodeColumn?: string | null;
}

export interface FieldDiff {
  fieldName: string;
  oldValue: string | null;
  newValue: string | null;
}

export type UpdateRowStatus = 'WillUpdate' | 'NoChanges' | 'Error' | 'Warning';

export interface TransactionUpdateRowPreviewResult {
  rowNumber: number;
  originalData: Record<string, string>;
  status: UpdateRowStatus;
  diffs: FieldDiff[];
  issues: { field: string; message: string; severity: 'Error' | 'Warning'; category: string }[];
  existingTransactionId: string | null;
}

export interface TransactionUpdateValidateResponse {
  totalRows: number;
  willUpdateCount: number;
  noChangesCount: number;
  errorCount: number;
  rowResults: TransactionUpdateRowPreviewResult[];
}

export interface TransactionUpdateExecuteAccepted {
  jobId: string;
}

export interface TransactionUpdateResult {
  updated: number;
  skippedNoChanges: number;
  skippedErrors: number;
}
