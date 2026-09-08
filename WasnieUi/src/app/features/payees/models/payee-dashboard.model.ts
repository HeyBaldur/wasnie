import { QuotaAttainment, QuotaSummary } from '../../quotas/models/quota.model';
// The SAME shape the dashboard uses. A payee-flavoured copy would let the two drift apart about what
// "paid" means, and these figures are only worth anything because they agree everywhere.
import { DashboardCommissionsBand } from '../../dashboard/models/dashboard.models';

export interface SalesTrendPoint {
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
  /** The range the server actually applied, echoed back. ISO yyyy-MM-dd. */
  from: string;
  to: string;
  /** Total / Paid / Unpaid for this payee over the range. */
  commissionsBand: DashboardCommissionsBand;
  attainmentItems: QuotaAttainment[];
  salesTrend: SalesTrendPoint[];
  recentQuotas: QuotaSummary[];
  recentAssignments: DashboardAssignment[];
}
