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

export interface DashboardActionBand {
  draftPayRunsCount: number;
  payoutsPendingApprovalCount: number;
  payoutsPendingApprovalByCurrency: CurrencyTotal[];
  payoutsApprovedUnpaidByCurrency: CurrencyTotal[];
  pendingByPlanItems: PlanPendingItem[];
  unprocessablePendingItems: UnprocessablePendingItem[];
  driftAlerts: DriftAlertItem[];
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
