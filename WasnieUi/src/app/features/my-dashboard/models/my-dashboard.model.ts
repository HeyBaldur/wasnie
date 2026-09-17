/**
 * KAN-92 — what the signed-in person sees about themselves.
 *
 * The shapes mirror the API exactly. Nothing here is computed in the browser: every figure arrives
 * already decided by the same services the pay run uses, because a screen that does its own money
 * arithmetic becomes a second source of truth about somebody's pay.
 */

/** Which zero a zero is. Sent as a token so the screen, not the engine, chooses the words. */
export type BalanceSemantic =
  | 'NothingRecorded'
  | 'EarningsAndNoDebt'
  | 'EarningsWithDebt'
  | 'DebtOnly'
  | 'DebtExceedsPending';

/**
 * Where an attainment ratio came from.
 *
 * `NoTarget` means nobody set a quota, NOT that the person sold nothing — the ratio is 0 either way
 * and the two readings are opposite.
 */
export type AttainmentSource = 'Measured' | 'Supplied' | 'Defaulted' | 'NoTarget';

/** What a quota is counted in. A Units target is a number of deals and has no currency. */
export type QuotaMeasurement = 'Revenue' | 'Units';

export interface MyCurrencyBalance {
  currency: string;
  earnedCommissionsInPeriod: number;
  paidOutInPeriod: number;
  disputedInPeriod: number;
  awaitingPaymentAllTime: number;
  outstandingDebt: number;
  netPendingPayout: number;
  interpretation: BalanceSemantic;
  clawbackCreditAllTime: number;
}

export interface MyLedgerSummary {
  payeeId: string;
  payeeName: string;
  periodLabel: string;
  periodStart: string | null;
  periodEnd: string | null;
  byCurrency: MyCurrencyBalance[];
}

export interface MyQuotaAttainment {
  quotaId: string;
  planName: string;
  targetAmount: number;
  currency: string;
  measurement: QuotaMeasurement;
  achievedAmount: number;
  attainmentRatio: number;
  attainmentSource: AttainmentSource;
  periodStart: string;
  periodEnd: string;
}

/**
 * `linked` is the honest half: false means this login was never attached to a payee record, so there
 * are no figures — not zero, NONE. Rendering 0.00 for that state would tell somebody who has earned
 * money that they have not.
 */
export interface MyDashboard {
  linked: boolean;
  payeeId: string | null;
  payeeName: string | null;
  summary: MyLedgerSummary | null;
  quotas: MyQuotaAttainment[];
}
