export interface TransactionImportColumnMapping {
  referenceNumberColumn: string;
  payeeCodeColumn?: string | null;
  amountColumn: string;
  currencyColumn: string;
  transactionDateColumn: string;
  externalIdColumn?: string | null;
  quantityColumn?: string | null;
  descriptionColumn?: string | null;
  productNameColumn?: string | null;
  productSkuColumn?: string | null;
}

export interface ParseResponse {
  fileId: string;
  headers: string[];
  rowCount: number;
  sampleRows: Record<string, string>[];
}

export type IssueCategory = 'Reference' | 'Format' | 'Required' | 'Other';

export interface ValidationIssue {
  field: string;
  message: string;
  severity: 'Error' | 'Warning';
  category: IssueCategory;
}

export interface TransactionRowValidationResult {
  rowNumber: number;
  originalData: Record<string, string>;
  issues: ValidationIssue[];
  hasErrors: boolean;
  hasWarnings: boolean;
}

export interface TransactionValidateResponse {
  totalRows: number;
  errorCount: number;
  warningCount: number;
  validRowCount: number;
  rowResults: TransactionRowValidationResult[];
}

export interface ExecuteAccepted {
  jobId: string;
}

export type JobState = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelling' | 'Cancelled';

export interface ImportJobStatus {
  id: string;
  state: JobState;
  progressCurrent: number;
  progressTotal: number;
  errorMessage: string | null;
  enqueuedAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
}

export interface TransactionImportResult {
  totalRows: number;
  processedRows: number;
}
