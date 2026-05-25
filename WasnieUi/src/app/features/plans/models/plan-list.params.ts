import { PlanStatus } from './plan.model';

export interface PlanListParams {
  page: number;
  pageSize: number;
  status: PlanStatus | null;
  search: string;
}
