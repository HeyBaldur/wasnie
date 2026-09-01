import { Rule } from './rule.model';

export type PlanStatus = 'Draft' | 'Active' | 'Archived';

export interface PlanSummary {
  id: string;
  name: string;
  version: number;
  status: PlanStatus;
  effectiveStart: string;
  effectiveEnd: string;
  currency: string;
  activeRuleCount: number;
  /** Active assignments. Archiving the plan deactivates every one of them. */
  activeAssignmentCount: number;
}

export type PlanVersion = PlanSummary;

export interface Plan {
  id: string;
  tenantId: string;
  name: string;
  description: string;
  version: number;
  status: PlanStatus;
  effectiveStart: string;
  effectiveEnd: string;
  currency: string;
  createdAt: string;
  createdBy: string;
  rules: Rule[];
  /** Active assignments. Archiving the plan deactivates every one of them. */
  activeAssignmentCount: number;
  /** Clawback policy. Null means this plan claws nothing back — the state every plan starts in. */
  clawbackMaturationDays: number | null;
  clawbackCapPercent: number | null;
}

export interface CreatePlanRequest {
  name: string;
  description: string;
  effectiveStart: string;
  effectiveEnd: string;
  currency: string;
}
