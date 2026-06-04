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
}

export interface QuotaListParams {
  page: number;
  pageSize: number;
  search: string;
  status: QuotaStatus | null;
}
