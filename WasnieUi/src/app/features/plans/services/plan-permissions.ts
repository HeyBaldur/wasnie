import { PlanStatus } from '../models/plan.model';

export interface PlanActionPermissions {
  canAddRule: boolean;
  canEditRule: boolean;
  canDeleteRule: boolean;
  canViewRule: boolean;
  canActivate: boolean;
  canArchive: boolean;
  canClone: boolean;
  canEditPlan: boolean;

  /**
   * The emergency brake: stop a rule of a LIVE plan without cloning it.
   *
   * ★ ACTIVE ONLY, AND THAT IS THE WHOLE POINT. A Draft's rules are edited and removed outright, so
   * there is nothing to brake; an Archived plan already generates nothing. This is the one rule
   * action that exists precisely BECAUSE the plan is live — the opposite of every other flag here.
   *
   * Status is only half the gate. The other half is the Plans.StopRule permission, checked with
   * *hasPermission in the template: RBAC hides, it never disables.
   */
  canStopRule: boolean;
}

export function getPlanPermissions(status: PlanStatus | null | undefined): PlanActionPermissions {
  switch (status) {
    case 'Draft':
      return {
        canAddRule: true,
        canEditRule: true,
        canDeleteRule: true,
        canViewRule: true,
        canActivate: true,
        canArchive: false,
        canClone: false,
        canEditPlan: true,
        canStopRule: false,
      };
    case 'Active':
      return {
        canAddRule: false,
        canEditRule: false,
        canDeleteRule: false,
        canViewRule: true,
        canActivate: false,
        canArchive: true,
        canClone: true,
        canEditPlan: false,
        canStopRule: true,
      };
    case 'Archived':
      return {
        canAddRule: false,
        canEditRule: false,
        canDeleteRule: false,
        canViewRule: true,
        canActivate: false,
        canArchive: false,
        canClone: true,
        canEditPlan: false,
        canStopRule: false,
      };
    default:
      return {
        canAddRule: false,
        canEditRule: false,
        canDeleteRule: false,
        canViewRule: false,
        canActivate: false,
        canArchive: false,
        canClone: false,
        canEditPlan: false,
        canStopRule: false,
      };
  }
}
