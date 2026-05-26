export type AssignmentStatus = 'Active' | 'Deactivated';

export interface Assignment {
  id: string;
  tenantId: string;
  planId: string;
  planName: string;
  planVersion: number;
  payeeId: string;
  payeeFullName: string;
  payeeEmployeeCode: string;
  effectiveStart: string;
  effectiveEnd: string;
  status: AssignmentStatus;
  notes: string | null;
  createdAt: string;
}

export interface CreateAssignmentRequest {
  planId: string;
  payeeId: string;
  effectiveStart: string;
  effectiveEnd: string;
  notes?: string | null;
}

export interface AssignmentListParams {
  page: number;
  pageSize: number;
  search: string;
  status: AssignmentStatus | null;
}
