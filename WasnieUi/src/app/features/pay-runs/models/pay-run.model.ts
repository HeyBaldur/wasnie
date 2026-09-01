import { PayoutListItem } from '../../payouts/models/payout.model';
import { PagedResult } from '../../../shared/models/pagination.models';

export type PayRunStatus = 'Draft' | 'Approved' | 'Paid';

export interface PayRunListItem {
  id: string;
  periodStart: string;
  periodEnd: string;
  status: PayRunStatus;
  supplementalSequence: number;
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
  /** Why nothing happened, when nothing happened. See `PayoutRunDiagnostics`. */
  diagnostics: PayoutRunDiagnostics;
  isSupplemental: boolean;
  supplementalSequence: number;
}

/**
 * What the engine actually did.
 *
 * ★ This screen used to turn `payoutsCreated === 0` into "No matching credits found for this period" —
 * a cause the backend never established. In the run that prompted this it was false twice: four
 * assignments were dropped for terminated payees, all twenty survivors hit an already-Paid payout, and
 * no credit was ever queried. These fields are what the message is built from now, and the message may
 * say nothing they do not support.
 */
export interface PayoutRunDiagnostics {
  /** Active assignments overlapping the period — the population, before any discard. */
  assignmentsConsidered: number;
  /**
   * How many got far enough for the engine to go looking for credits. While this is 0, the words
   * "no matching credits" are unsayable: none were queried.
   */
  assignmentsReachingCreditLookup: number;
  creditsExamined: number;
  /** One entry per reason that discarded at least one assignment. Reasons that discarded nothing are absent. */
  skipped: PayoutSkipCount[];
}

/**
 * A reason code and how many assignments it discarded.
 *
 * ★ THE CODE IS LOOKED UP, NEVER PRINTED. It is a backend identifier; the words live in EN/ES/PL here.
 * A code with no translation degrades to a neutral line that says an assignment was skipped and does
 * NOT guess why — the whole point of this change is that the screen stops inventing causes.
 */
export interface PayoutSkipCount {
  code: string;
  count: number;
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
  supplementalSequence: number;
  payeeCount: number;
  totalAmounts: Record<string, number>;
  approvedAt: string | null;
  paidAt: string | null;
}
