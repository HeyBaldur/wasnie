export interface PayeeImportColumnMapping {
  fullNameColumns: string[];
  employeeCodeColumn: string;
  emailColumn: string;
  hireDateColumn: string;
  roleColumn?: string | null;
  managerEmployeeCodeColumn?: string | null;
  employmentTypeColumn?: string | null;
  locationColumn?: string | null;
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

export interface PayeeRowValidationResult {
  rowNumber: number;
  originalData: Record<string, string>;
  issues: ValidationIssue[];
  hasErrors: boolean;
  hasWarnings: boolean;
}

export interface ValidateResponse {
  totalRows: number;
  errorCount: number;
  warningCount: number;
  validRowCount: number;
  rowResults: PayeeRowValidationResult[];
}

export interface PayeeImportResult {
  totalRows: number;
  createdCount: number;
  skippedCount: number;
  skippedRows: PayeeRowValidationResult[];
  blocked?: boolean;
  blockedCurrent?: number;
  blockedIncoming?: number;
  blockedLimit?: number;
  blockedTier?: string;
}

export interface WizardState {
  fileId: string | null;
  fileName: string | null;
  fileSize: number | null;
  headers: string[];
  rowCount: number;
  sampleRows: Record<string, string>[];
  columnMapping: PayeeImportColumnMapping | null;
  validateResponse: ValidateResponse | null;
  importResult: PayeeImportResult | null;
}
