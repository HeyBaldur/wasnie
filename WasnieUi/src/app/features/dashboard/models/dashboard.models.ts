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

export interface DashboardActionBand {
  draftPayRunsCount: number;
  payoutsPendingApprovalCount: number;
  payoutsPendingApprovalByCurrency: CurrencyTotal[];
  payoutsApprovedUnpaidByCurrency: CurrencyTotal[];
  pendingByPlanItems: PlanPendingItem[];
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
