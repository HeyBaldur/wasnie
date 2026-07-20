import { Pipe, PipeTransform } from '@angular/core';
import type { BadgeVariant } from '../ui';

/**
 * SINGLE SOURCE OF TRUTH for how a quota's PERSISTED lifecycle status (Draft/Active/Closed) is shown:
 * its badge colour (variant) and its i18n label key (QUOTAS.STATUS_*). The quota detail, the quotas list
 * and the payee-profile quotas panel all use these — so the displayed status can never diverge between
 * screens again (the bug this consolidates: the payee profile used to derive a TEMPORAL phase from dates
 * instead of reading the real status).
 *
 * NOTE: this is the lifecycle STATUS, not a temporal "is the period running now" phase. Don't feed period
 * dates here.
 */
export function quotaStatusVariant(status: string | null | undefined): BadgeVariant {
  switch (status) {
    case 'Active': return 'success';
    case 'Closed': return 'warning';
    case 'Draft': return 'neutral';
    default: return 'neutral';
  }
}

export function quotaStatusLabelKey(status: string | null | undefined): string {
  return status ? `QUOTAS.STATUS_${status.toUpperCase()}` : 'QUOTAS.STATUS_DRAFT';
}

/** Badge colour for a quota's persisted status. Usage: `[variant]="quota.status | quotaStatusVariant"`. */
@Pipe({ name: 'quotaStatusVariant', standalone: true })
export class QuotaStatusVariantPipe implements PipeTransform {
  transform(status: string | null | undefined): BadgeVariant {
    return quotaStatusVariant(status);
  }
}

/** i18n label key for a quota's persisted status. Usage: `quota.status | quotaStatusLabel | translate`. */
@Pipe({ name: 'quotaStatusLabel', standalone: true })
export class QuotaStatusLabelPipe implements PipeTransform {
  transform(status: string | null | undefined): string {
    return quotaStatusLabelKey(status);
  }
}
