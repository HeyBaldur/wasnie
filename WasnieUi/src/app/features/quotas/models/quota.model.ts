export type QuotaStatus = 'Draft' | 'Active' | 'Closed';

export enum QuotaMeasurementType {
  Revenue = 0,
  Margin = 1,
  Units = 2,
  ACV = 3,
  Bookings = 4,
}

export interface QuotaSummary {
  id: string;
  tenantId: string;
  payeeId: string;
  payeeName: string;
  payeeEmployeeCode: string;
  planId: string;
  planName: string;
  measurementType: QuotaMeasurementType;
  amount: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
  status: QuotaStatus;
  notes: string | null;
  createdAt: string;
  isCurrencyValid: boolean;
  planCurrency: string;
}

export interface CreateQuotaRequest {
  payeeId: string;
  planId: string;
  measurementType: QuotaMeasurementType;
  amount: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
  notes?: string | null;
}

/**
 * One quota configuration for N payees. A superset of {@link CreateQuotaRequest} with the single
 * payee replaced by a list — there is no second way to describe a quota.
 */
export interface BulkCreateQuotasRequest {
  payeeIds: string[];
  planId: string;
  measurementType: QuotaMeasurementType;
  amount: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
  notes?: string | null;
}

/** Why one payee of a batch could not receive the quota. Carries the name: the admin picked people. */
export interface BulkQuotaFailure {
  payeeId: string;
  payeeName: string;
  payeeEmployeeCode: string;
  reason: string;
}

/**
 * All-or-nothing: exactly one of the two lists is populated. A rejected batch created NOTHING, which
 * is what makes "fix the reasons and send it again" safe — a partial success would duplicate the
 * quotas that had already been created.
 */
export interface BulkCreateQuotasResult {
  created: QuotaSummary[];
  failures: BulkQuotaFailure[];
}

export interface UpdateQuotaRequest {
  quotaId: string;
  measurementType: QuotaMeasurementType;
  amount: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
  notes?: string | null;
}

export interface QuotaAttainment {
  quotaId: string;
  planId: string;
  planName: string;
  measurementType: QuotaMeasurementType;
  targetAmount: number;
  currency: string;
  achievedAmount: number;
  attainmentValue: number;
  attainmentPercent: string;
  periodStart: string;
  periodEnd: string;
  status: QuotaStatus;
  isCurrencyValid: boolean;
  planCurrency: string;
}

export interface QuotaListParams {
  page: number;
  pageSize: number;
  search: string;
  status: QuotaStatus | null;
}
