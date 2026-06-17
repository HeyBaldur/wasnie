import { PayoutListItem } from '../../payouts/models/payout.model';
import { PagedResult } from '../../../shared/models/pagination.models';

export type PayRunStatus = 'Draft' | 'Approved' | 'Paid';

export interface PayRunListItem {
  id: string;
  periodStart: string;
  periodEnd: string;
  status: PayRunStatus;
  payeeCount: number;
  paidPayeeCount: number;
  zeroPayoutCount: number;
  totalAmounts: Record<string, number>;
  createdAt: string;
  createdBy: string;
  approvedAt: string | null;
  approvedBy: string | null;
  paidAt: string | null;
  paidBy: string | null;
}

export interface PayRunDetail extends PayRunListItem {
  payouts: PagedResult<PayoutListItem>;
}

export interface PayRunPayoutsDetailFilter {
  status: string | null;
  periodFrom: string | null;
  periodTo: string | null;
  amountMin: number | null;
  amountMax: number | null;
  payeeIds: string[];
  planIds: string[];
  currencies: string[];
  excludeZero: boolean;
}

export const EMPTY_PAYOUTS_DETAIL_FILTER: PayRunPayoutsDetailFilter = {
  status: null,
  periodFrom: null,
  periodTo: null,
  amountMin: null,
  amountMax: null,
  payeeIds: [],
  planIds: [],
  currencies: [],
  excludeZero: true,
};

export interface CalculatePayRunRequest {
  periodStart: string;
  periodEnd: string;
}

export interface CalculatePayRunResult {
  payRunId: string;
  payoutsCreated: number;
  conflicts: PayRunConflict[];
  warnings: PayRunWarning[];
}

export interface PayRunConflict {
  payeeId: string;
  payeeName: string;
  planId: string;
  periodStart: string;
  periodEnd: string;
  status: string;
}

export interface PayRunWarning {
  payeeId: string;
  payeeName: string;
  planId: string;
  periodStart: string;
  periodEnd: string;
  pendingTransactionCount: number;
}

export interface OverlappingPayRun {
  id: string;
  periodStart: string;
  periodEnd: string;
  status: PayRunStatus;
  payeeCount: number;
  totalAmounts: Record<string, number>;
  approvedAt: string | null;
  paidAt: string | null;
}
