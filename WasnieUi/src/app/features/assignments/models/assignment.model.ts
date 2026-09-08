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

export interface BulkAssignmentIdsRequest {
  assignmentIds: string[];
}

export interface BulkAssignmentOperationResult {
  affected: number;
  errors: string[];
}

export interface BlockedAssignmentDto {
  assignmentId: string;
  payeeName: string;
  planName: string;
  effectiveStart: string;
  effectiveEnd: string;
  reason: string;
}

export interface BulkDeleteAssignmentsResult {
  allDeleted: boolean;
  deletedCount: number;
  blocked: BlockedAssignmentDto[];
}

/**
 * What deactivating a set of assignments would put out of reach of every pay run.
 *
 * ★ IT IS ASKED BEFORE THE ACT, and an empty answer is the normal one. Most deactivations strand
 *   nothing; the dialog only grows the warning when there is money behind it, because a dialog that
 *   warns every time stops being read.
 */
export interface DeactivationImpact {
  strandedByCurrency: { amount: number; currency: string }[];
  items: DeactivationImpactItem[];
}

export interface DeactivationImpactItem {
  assignmentId: string;
  payeeId: string;
  payeeName: string;
  planId: string;
  planName: string;
  creditCount: number;
  amount: number;
  currency: string;
}
