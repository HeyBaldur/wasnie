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

// ── The temporal complement: the period ran out, the status did not change ───────────────────────
// A quota's status is NEVER derived from its dates — Draft → Active → Closed moves only by an
// explicit action, so a quota whose period ended last month is genuinely still Active until someone
// closes it. That is deliberate domain behaviour and nothing here changes it.
//
// What it leaves behind is a reading problem: a row that says "Active" and nothing else looks like a
// target still being worked towards. This is the missing half of the sentence — shown NEXT TO the
// status badge, never instead of it.

/** Today as a local YYYY-MM-DD string, the same shape the API sends for a DateOnly period. */
function todayIsoDate(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}

/**
 * True when an ACTIVE quota's period has already ended.
 *
 * DATE, not datetime. Periods arrive as `DateOnly` ("2026-06-30"), so both sides are compared as
 * YYYY-MM-DD strings — which sorts chronologically and carries no time, no timezone and no
 * conversion. Building a `Date` from the string instead would parse it as UTC midnight and then read
 * it back in local time, so west of Greenwich the last day of the period would show as expired for
 * part of the day.
 *
 * The comparison is strict: a quota ending TODAY is still running, not expired.
 */
export function isQuotaPeriodExpired(
  status: string | null | undefined,
  periodEnd: string | null | undefined,
): boolean {
  // Only Active can be "expired but still open". Draft never started; Closed already says it is over.
  if (status !== 'Active') return false;
  if (!periodEnd) return false;

  const end = periodEnd.slice(0, 10); // tolerates an ISO datetime if the contract ever widens
  if (end.length !== 10) return false; // unparseable → say nothing rather than guess

  return end < todayIsoDate();
}

/**
 * Usage: `quota.periodEnd | quotaPeriodExpired: quota.status`.
 *
 * Pure: it recomputes when the quota changes, not on every change-detection pass. The only input it
 * does not track is the clock, so a page left open across midnight keeps yesterday's answer until the
 * next navigation — a trade the alternative (an impure pipe running on every CD cycle, on every row)
 * does not justify.
 */
@Pipe({ name: 'quotaPeriodExpired', standalone: true })
export class QuotaPeriodExpiredPipe implements PipeTransform {
  transform(periodEnd: string | null | undefined, status: string | null | undefined): boolean {
    return isQuotaPeriodExpired(status, periodEnd);
  }
}
