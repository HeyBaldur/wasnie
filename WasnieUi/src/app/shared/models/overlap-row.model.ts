import type { BadgeVariant } from '../ui';

export interface OverlapAmountEntry {
  amount: number;
  currency: string;
}

export interface OverlapRow {
  id: string;
  periodStart: string;
  periodEnd: string;
  statusLabel: string;
  statusVariant: BadgeVariant;
  col3: string;
  amounts: OverlapAmountEntry[];
}
