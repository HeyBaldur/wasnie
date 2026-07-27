export interface CurrencyTotal {
  amount: number;
  currency: string;
}

export interface PlanPendingItem {
  planId: string;
  planName: string;
  currency: string;
  pendingCount: number;
}

/** A reason a Pending transaction can't be processed yet. `currencies` is set only for CurrencyMismatch. */
export interface UnprocessablePendingItem {
  reason: 'NoPayee' | 'CurrencyMismatch' | 'NoActiveAssignment';
  count: number;
  currencies: string[];
}

/**
 * A payee whose transactions are blocked because their plan can't be determined: the payee has 2+
 * eligible plans and nobody said which applies, so the engine refuses to guess.
 *
 * Grouped by payee because that's the unit of the FIX — one payee's overlapping assignments block all
 * of their transactions at once, and resolving the overlap unblocks them together.
 */
export interface AmbiguousAttributionPayee {
  payeeId: string;
  payeeName: string;
  employeeCode: string | null;
  transactionCount: number;
  planNames: string[];
}

/**
 * A CRM drift alert: a HubSpot deal changed (amount and/or close date) AFTER its transaction was already
 * Calculated/Paid — money that already moved, not auto-corrected, flagged for manual review. Different in
 * kind from `UnprocessablePendingItem` (which is "can't process yet"). Deep-link target = `referenceNumber`.
 */
export interface DriftAlertItem {
  transactionId: string;
  referenceNumber: string;
  externalDealId: string;
  transactionStatus: 'Calculated' | 'Paid';
  amountChanged: boolean;
  oldAmount: number;
  oldCurrency: string;
  newAmount: number;
  newCurrency: string;
  dateChanged: boolean;
  oldCloseDate: string;
  newCloseDate: string;
  detectedAt: string;
}

/**
 * A deal-lost alert: a HubSpot deal Wasnie already commissioned is NO LONGER closed-won (moved to Lost or
 * an open stage) after its transaction was Calculated/Paid. Distinct from `DriftAlertItem` (amount/date
 * change on a STILL-won deal). Calculated → the UI offers "Revert commission"; Paid → informational only
 * (clawback of paid money is handled outside for now). `commissionAmount` is what a revert takes back.
 */
export interface DealLostAlertItem {
  transactionId: string;
  referenceNumber: string;
  externalDealId: string;
  transactionStatus: 'Calculated' | 'Paid';
  commissionAmount: number;
  commissionCurrency: string;
  detectedAt: string;
}

export interface DashboardActionBand {
  draftPayRunsCount: number;
  payoutsPendingApprovalCount: number;
  payoutsPendingApprovalByCurrency: CurrencyTotal[];
  payoutsApprovedUnpaidByCurrency: CurrencyTotal[];
  pendingByPlanItems: PlanPendingItem[];
  unprocessablePendingItems: UnprocessablePendingItem[];
  driftAlerts: DriftAlertItem[];
  dealLostAlerts: DealLostAlertItem[];
  ambiguousAttributionPayees: AmbiguousAttributionPayee[];
}

export interface DashboardPeriodBand {
  transactionsCount: number;
  transactionsVolumeByCurrency: CurrencyTotal[];
  payoutsTotalByCurrency: CurrencyTotal[];
  creditsCount: number;
  creditsTotalByCurrency: CurrencyTotal[];
  avgQuotaAttainmentPercent: number | null;
  activePlansCount: number;
  activeQuotasCount: number;
  payeesActiveCount: number;
  payeesInactiveCount: number;
}

export interface DashboardTrendPoint {
  currency: string;
  currentAmount: number;
  priorAmount: number;
  changePercent: number | null;
  direction: 'up' | 'down' | 'neutral';
}

export interface DashboardTrendBand {
  currentPeriodLabel: string;
  priorPeriodLabel: string;
  commissionTrend: DashboardTrendPoint[];
}

export interface DashboardActivityItem {
  timestampUtc: string;
  actorEmail: string;
  actorInitials: string;
  action: string;
  resourceType: string;
  resourceDisplayName: string | null;
}

export interface DashboardSummary {
  periodLabel: string;
  actionBand: DashboardActionBand;
  periodBand: DashboardPeriodBand;
  trendBand: DashboardTrendBand | null;
  activityFeed: DashboardActivityItem[];
}
