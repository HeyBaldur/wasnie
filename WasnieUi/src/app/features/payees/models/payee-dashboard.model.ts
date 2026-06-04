import { QuotaAttainment, QuotaSummary } from '../../quotas/models/quota.model';

export interface EarningsTrendPoint {
  year: number;
  month: number;
  monthLabel: string;
  amount: number;
  currency: string;
}

export interface DashboardAssignment {
  id: string;
  planId: string;
  planName: string;
  planVersion: number;
  effectiveStart: string;
  effectiveEnd: string;
  status: string;
}

export interface PayeeDashboard {
  attainmentItems: QuotaAttainment[];
  earningsTrend: EarningsTrendPoint[];
  recentQuotas: QuotaSummary[];
  recentAssignments: DashboardAssignment[];
}
