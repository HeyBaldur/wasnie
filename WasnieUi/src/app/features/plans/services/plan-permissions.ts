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
      };
  }
}
