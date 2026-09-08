import { QuotaAttainment, QuotaSummary } from '../../quotas/models/quota.model';
// The SAME shape the dashboard uses. A payee-flavoured copy would let the two drift apart about what
// "paid" means, and these figures are only worth anything because they agree everywhere.
import { DashboardCommissionsBand, CurrencyTotal } from '../../dashboard/models/dashboard.models';

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

/**
 * Commission a payee is owed that no pay run can reach, grouped by the plan it sits on.
 *
 * ★ IT CARRIES THE FIX, NOT ONLY THE FIGURE. An amount alone is what sent an administrator through a
 * pay run, a credits list and the database before discovering that six deactivated assignments were
 * the whole story. Each group names the assignment to reactivate and the dates its transactions fall
 * in, because reactivating is not always enough — an assignment that ended before those transactions
 * still will not pay them.
 */
export interface PayeeUnreachableCommission {
  totalByCurrency: CurrencyTotal[];
  groups: UnreachableCommissionGroup[];
}

export interface UnreachableCommissionGroup {
  planId: string;
  planName: string;
  planStatus: string;
  creditCount: number;
  amount: number;
  currency: string;
  /** A CODE — 'AssignmentDeactivated' | 'NoAssignment' | 'PlanArchived'. Translated by whitelist (§C2). */
  reason: string;
  assignmentId: string | null;
  effectiveStart: string | null;
  effectiveEnd: string | null;
  /** The window the underlying transactions fall in — which pay run period to run afterwards. */
  coversTransactionsFrom: string | null;
  coversTransactionsTo: string | null;
}
