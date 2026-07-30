import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, LowerCasePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { RefreshOnEnterDirective } from '../../shared/directives/refresh-on-enter.directive';
import { CurrencyFormatPipe } from '../../shared/pipes/currency-format.pipe';
import { HasPermissionPipe } from '../../shared/pipes/has-permission.pipe';
import { DashboardStore } from './store/dashboard.store';
import { CurrencyTotal, DashboardTrendPoint, UnprocessablePendingItem, DriftAlertItem, DealLostAlertItem, AmbiguousAttributionPayee } from './models/dashboard.models';
import { TransactionsApiService } from '../transactions/services/transactions.api.service';
import { TerminatedAccountsStore } from '../ledger/state/terminated-accounts.store';
import { ToastService } from '../../shared/services/toast.service';
import { extractApiError } from '../../shared/utils/api-error';
import {
  WsCardComponent,
  WsBadgeComponent,
  WsPageLayoutComponent,
  WsSegmentedControlComponent,
  WsStatCardComponent,
  WsGaugeComponent,
  WsBarChartComponent,
  WsSparklineChartComponent,
  WsHBarChartComponent,
  WsButtonComponent,
  WsConfirmationModalComponent,
  type SegOption,
  type CardAccent,
  type BarChartPoint,
} from '../../shared/ui';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    RouterLink,
    DecimalPipe,
    LowerCasePipe,
    TranslatePipe,
    AppShellComponent,
    RefreshOnEnterDirective,
    IconComponent,
    CurrencyFormatPipe,
    HasPermissionPipe,
    WsCardComponent,
    WsBadgeComponent,
    WsPageLayoutComponent,
    WsSegmentedControlComponent,
    WsStatCardComponent,
    WsGaugeComponent,
    WsBarChartComponent,
    WsSparklineChartComponent,
    WsHBarChartComponent,
    WsButtonComponent,
    WsConfirmationModalComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  readonly store = inject(DashboardStore);
  private readonly transactionsApi = inject(TransactionsApiService);
  private readonly toast = inject(ToastService);

  /**
   * Departed payees whose account is still open. It rides on its own endpoint rather than the
   * dashboard summary because that endpoint — and the definition of "still open" — already exists;
   * the card counts the rows the server returned and splits them by the sign it stored.
   *
   * Why it belongs in "Requires action": the engine excludes terminated payees from every pay run,
   * which is correct and also means nothing will ever surface these accounts on its own. Money owed
   * in either direction would sit there indefinitely with no screen ever mentioning it.
   */
  readonly terminated = inject(TerminatedAccountsStore);

  constructor() {
    void this.terminated.load();
  }

  // The deal-lost alert the admin is confirming a revert for (drives the confirmation modal), and whether
  // the revert call is in flight.
  readonly revertTarget = signal<DealLostAlertItem | null>(null);
  readonly reverting = signal(false);

  /**
   * Sparkline values — period total distributed across 7 proportional points.
   * Illustrative only: shows the shape of a typical accumulation curve for
   * the period. No day-level labels are attached so tooltips never claim a
   * specific date had a specific value.
   * Returns [] when there is no data — the template hides the chart entirely.
   */
  readonly sparklinePayouts = computed<number[]>(() => {
    const total = this.store.periodBand()?.payoutsTotalByCurrency?.[0]?.amount ?? 0;
    if (total === 0) return [];
    const unit = total / 7;
    return [0.82, 0.98, 0.91, 1.10, 1.02, 1.14, 1.22].map(f => Math.round(unit * f));
  });

  readonly sparklineTransactions = computed<number[]>(() => {
    const count = this.store.periodBand()?.transactionsCount ?? 0;
    if (count === 0) return [];
    const unit = count / 7;
    return [0.88, 1.14, 0.97, 1.42, 1.26, 1.59, 1.75].map(f => Math.round(unit * f));
  });

  readonly sparklineCredits = computed<number[]>(() => {
    const count = this.store.periodBand()?.creditsCount ?? 0;
    if (count === 0) return [];
    const unit = count / 7;
    return [0.59, 1.03, 0.82, 1.24, 1.12, 1.50, 1.71].map(f => Math.round(unit * f));
  });

  // ── Period-aware query params for period band links ───────────────────────
  // Each computes the correct filter params for the destination list so the
  // data shown on arrival matches exactly what the dashboard card displays.

  readonly payoutsLinkParams = computed(() => {
    const key = this.store.period();
    const { from, to } = this._periodDates(key);
    const p: Record<string, string> = { period: key };
    if (from) p['pFrom'] = from;
    if (to) p['pTo'] = to;
    return p;
  });

  readonly transactionsLinkParams = computed(() => {
    const { from, to } = this._periodDates(this.store.period());
    const p: Record<string, string> = {};
    if (from) p['txFrom'] = from;
    if (to) p['txTo'] = to;
    return p;
  });

  readonly creditsLinkParams = computed(() => {
    const { from, to } = this._periodDates(this.store.period());
    const p: Record<string, string> = {};
    if (from) p['allocFrom'] = from;
    if (to) p['allocTo'] = to;
    return p;
  });

  /**
   * Mirrors PeriodHelper.ComputeDateRange on the backend.
   * this-month: first of month → today
   * last-month: first of prev month → last day of prev month
   * ytd: Jan 1 → today
   * all-time: null, null
   */
  _periodDates(key: string): { from: string | null; to: string | null } {
    const today = new Date();
    const yyyy = today.getFullYear();
    const mm = String(today.getMonth() + 1).padStart(2, '0');
    const dd = String(today.getDate()).padStart(2, '0');
    const todayStr = `${yyyy}-${mm}-${dd}`;
    switch (key) {
      case 'this-month':
        return { from: `${yyyy}-${mm}-01`, to: todayStr };
      case 'last-month': {
        const first = new Date(yyyy, today.getMonth() - 1, 1);
        const last = new Date(yyyy, today.getMonth(), 0);
        const fy = first.getFullYear();
        const fm = String(first.getMonth() + 1).padStart(2, '0');
        const ly = last.getFullYear();
        const lm = String(last.getMonth() + 1).padStart(2, '0');
        const ld = String(last.getDate()).padStart(2, '0');
        return { from: `${fy}-${fm}-01`, to: `${ly}-${lm}-${ld}` };
      }
      case 'ytd':
        return { from: `${yyyy}-01-01`, to: todayStr };
      default:
        return { from: null, to: null };
    }
  }

  readonly periodOptions: SegOption[] = [
    { value: 'this-month', label: 'DASHBOARD.PERIOD_THIS_MONTH' },
    { value: 'last-month', label: 'DASHBOARD.PERIOD_LAST_MONTH' },
    { value: 'ytd', label: 'DASHBOARD.PERIOD_YTD' },
    { value: 'all-time', label: 'DASHBOARD.PERIOD_ALL_TIME' },
  ];

  onPeriodChange(value: string): void {
    this.store.setPeriod(value);
  }

  actionCardAccent(count: number): CardAccent {
    return count > 0 ? 'warning' : 'none';
  }

  amountsAccent(totals: CurrencyTotal[]): CardAccent {
    return totals.length > 0 ? 'warning' : 'none';
  }

  trendIcon(direction: 'up' | 'down' | 'neutral'): string {
    return direction === 'neutral' ? 'trend-neutral' : 'trend-up';
  }

  /** True when the change% is either null (prior=0) or absurdly large (near-zero prior). */
  trendIsNoBase(point: DashboardTrendPoint): boolean {
    return point.changePercent === null || Math.abs(point.changePercent) > 500;
  }

  /** Safe formatted change % — only call when trendIsNoBase() returns false. */
  trendChangeFormatted(point: DashboardTrendPoint): string {
    const v = point.changePercent!;
    return `${v > 0 ? '+' : ''}${v.toFixed(1)}%`;
  }

  /**
   * Compact notation for monetary values that could be in the billions/trillions.
   * €5,929,711,576,736 → €5.93T  |  €94,564 → €94.56K  |  €1,234,567 → €1.23M
   */
  fmtCompact(amount: number, currency: string): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency,
      notation: 'compact',
      maximumFractionDigits: 2,
    }).format(amount);
  }

  /** Two-bar chart data: [current, prior] for a trend point. */
  trendBarPoints(point: DashboardTrendPoint, currentLabel: string, priorLabel: string): BarChartPoint[] {
    return [
      { label: priorLabel, value: point.priorAmount, currency: point.currency },
      { label: currentLabel, value: point.currentAmount, currency: point.currency, isCurrent: true },
    ];
  }

  /**
   * True when the audit entry was written by a background process, not a user
   * (e.g. HUBSPOT_TOKEN_REFRESHED). Those rows carry an empty ActorEmail, which
   * left the feed with a blank avatar and a blank actor name.
   */
  isSystemActor(email: string | null | undefined): boolean {
    return !email || email.trim().length === 0;
  }

  /** "admin@domain.com" → "admin" (max 18 chars, then ellipsis). */
  actorShortName(email: string): string {
    const at = email.indexOf('@');
    const name = at > 0 ? email.slice(0, at) : email;
    return name.length > 18 ? `${name.slice(0, 17)}…` : name;
  }

  /** "pending_transactions_processed" → "pending transactions" (max 3 words). */
  formatActivityAction(raw: string): string {
    return raw.replace(/_/g, ' ').split(' ').slice(0, 3).join(' ');
  }

  /** Truncate resource display name to 28 chars. */
  shortResource(name: string | null): string | null {
    if (!name) return null;
    return name.length > 28 ? `${name.slice(0, 26)}…` : name;
  }

  /** Total pending transaction count across all plans (for the pending-by-plan card badge). */
  pendingByPlanTotalCount(): number {
    return this.store.actionBand()?.pendingByPlanItems?.reduce((s, x) => s + x.pendingCount, 0) ?? 0;
  }

  // ── "Transactions that need attention" card ───────────────────────────────

  /** Total Pending transactions that can't be processed yet (sum across reasons). */
  attentionTotalCount(): number {
    return this.store.actionBand()?.unprocessablePendingItems?.reduce((s, x) => s + x.count, 0) ?? 0;
  }

  /** Per-reason label / explanation / icon for a row. */
  attentionMeta(reason: string): { labelKey: string; descKey: string; icon: string } {
    switch (reason) {
      case 'NoPayee':
        return { labelKey: 'DASHBOARD.ATTENTION_NOPAYEE_LABEL', descKey: 'DASHBOARD.ATTENTION_NOPAYEE_DESC', icon: 'users' };
      case 'CurrencyMismatch':
        return { labelKey: 'DASHBOARD.ATTENTION_CURRENCY_LABEL', descKey: 'DASHBOARD.ATTENTION_CURRENCY_DESC', icon: 'coin' };
      case 'NoActiveAssignment':
        return { labelKey: 'DASHBOARD.ATTENTION_NOASSIGN_LABEL', descKey: 'DASHBOARD.ATTENTION_NOASSIGN_DESC', icon: 'briefcase' };
      default:
        return { labelKey: reason, descKey: '', icon: 'alert-circle' };
    }
  }

  /**
   * Deep-link to Transactions filtered to EXACTLY this reason. `attention` drives a server-side filter
   * that uses the same classification as the dashboard count, so the list count matches the card. The
   * `statuses=Pending` is cosmetic (all reasons are Pending) so the Pending tab reads as selected.
   */
  attentionLinkParams(item: UnprocessablePendingItem): Record<string, string> {
    return { statuses: 'Pending', attention: item.reason };
  }

  // ── Drift alerts (a deal changed in HubSpot AFTER its commission was calculated/paid) ──────────────

  /** Unresolved CRM drift alerts to surface in the card (already money — distinct from "can't process"). */
  driftAlerts(): DriftAlertItem[] {
    return this.store.actionBand()?.driftAlerts ?? [];
  }

  // ── Deal-lost alerts (a deal left closed-won AFTER its commission was calculated/paid) ─────────────

  /** Unresolved deal-lost alerts to surface. Calculated ones are actionable (revert); Paid are informative. */
  dealLostAlerts(): DealLostAlertItem[] {
    return this.store.actionBand()?.dealLostAlerts ?? [];
  }

  /** Only a Calculated commission can be reverted here; Paid is shown but has no action (clawback is separate). */
  canRevert(alert: DealLostAlertItem): boolean {
    // transactionStatus is the LIVE status now (the server joins the transaction), so a commission paid
    // after the alert was raised no longer offers a revert the backend would refuse anyway.
    return alert.transactionStatus === 'Calculated';
  }

  /**
   * What the row SAYS, which has to track what the row OFFERS. An unpaid commission can be reverted; a
   * paid one cannot, and the honest sentence then depends on whether the clawback already ran.
   */
  dealLostActionKey(alert: DealLostAlertItem): string {
    if (alert.transactionStatus === 'Calculated') return 'DASHBOARD.DEAL_LOST_ACTION_CALCULATED';
    if (alert.transactionStatus !== 'Paid') return 'DASHBOARD.DEAL_LOST_ACTION_OTHER';
    return alert.clawbackState === 'Applied'
      ? 'DASHBOARD.DEAL_LOST_ACTION_PAID_CLAWBACK_APPLIED'
      : 'DASHBOARD.DEAL_LOST_ACTION_PAID_CLAWBACK_PENDING';
  }

  /** Open the confirmation modal for reverting this alert's commission. */
  askRevert(alert: DealLostAlertItem): void {
    this.revertTarget.set(alert);
  }

  cancelRevert(): void {
    this.revertTarget.set(null);
  }

  /** Confirmed: revert the commission, then reload the dashboard so the alert clears. */
  confirmRevert(): void {
    const target = this.revertTarget();
    if (!target || this.reverting()) return;
    this.reverting.set(true);
    this.transactionsApi.revertLostDeal(target.transactionId).subscribe({
      next: async () => {
        this.toast.show('DASHBOARD.DEAL_LOST_REVERTED', 'success');
        this.revertTarget.set(null);
        this.reverting.set(false);
        await this.store.reload();
      },
      error: (err) => {
        this.toast.show(extractApiError(err), 'error');
        this.reverting.set(false);
      },
    });
  }

  // ── Ambiguous attribution (payee on 2+ eligible plans, no plan declared) ───────────────────────

  /**
   * Payees whose transactions are blocked because their plan can't be determined. One row per PAYEE,
   * not per transaction: the overlapping assignments are the cause, so that's what the admin fixes.
   */
  ambiguousAttributionPayees(): AmbiguousAttributionPayee[] {
    return this.store.actionBand()?.ambiguousAttributionPayees ?? [];
  }

  /** Deep-link to the payee's assignments — where the overlap that caused the block can be resolved. */
  ambiguousLinkParams(item: AmbiguousAttributionPayee): unknown[] {
    return ['/payees', item.payeeId];
  }

  /** True when the card has anything to show (unprocessable reasons, drift/deal-lost alerts, or ambiguity). */
  hasAttentionItems(): boolean {
    return (this.store.actionBand()?.unprocessablePendingItems?.length ?? 0) > 0
      || this.driftAlerts().length > 0
      || this.dealLostAlerts().length > 0
      || this.ambiguousAttributionPayees().length > 0;
  }

  /**
   * Header badge total: unprocessable transactions + each drift alert + each BLOCKED TRANSACTION from
   * ambiguity (not each payee) — the badge counts things needing attention, and 43 blocked
   * transactions are 43 stuck items even though they're one row and one fix.
   */
  attentionBadgeTotal(): number {
    return this.attentionTotalCount()
      + this.driftAlerts().length
      + this.dealLostAlerts().length
      + this.ambiguousAttributionPayees().reduce((s, x) => s + x.transactionCount, 0);
  }

  /** i18n key for the commission state of a lost-deal transaction (calculated vs paid). */
  dealLostStatusKey(status: string): string {
    return status === 'Paid' ? 'DASHBOARD.DRIFT_STATE_PAID' : 'DASHBOARD.DRIFT_STATE_CALCULATED';
  }

  /** i18n key for the commission state of a drifted transaction (already calculated vs already paid). */
  driftStatusKey(status: string): string {
    return status === 'Paid' ? 'DASHBOARD.DRIFT_STATE_PAID' : 'DASHBOARD.DRIFT_STATE_CALCULATED';
  }

  /** Deep-link to the affected transaction via its reference (shared by drift + deal-lost rows). */
  driftLinkParams(alert: { referenceNumber: string }): Record<string, string> {
    return { ref: alert.referenceNumber };
  }

  /**
   * Total count of action items needing attention (for band header badge). Orphaned accounts count
   * too: leaving them out of the badge would put the card in a band whose header claims there is
   * nothing to do.
   */
  pendingActionCount(): number {
    const b = this.store.actionBand();
    if (!b) return this.terminated.count();
    return (
      b.draftPayRunsCount +
      b.payoutsPendingApprovalCount +
      (b.payoutsApprovedUnpaidByCurrency.length > 0 ? 1 : 0) +
      b.pendingByPlanItems.reduce((s, x) => s + x.pendingCount, 0) +
      this.terminated.count()
    );
  }

  relativeTime(isoUtc: string): string {
    // C# DateTime serialization can omit 'Z'; without it JS parses as local time instead of UTC
    const utcStr = /Z$|[+-]\d{2}:\d{2}$/.test(isoUtc) ? isoUtc : isoUtc + 'Z';
    const diff = Date.now() - new Date(utcStr).getTime();
    const mins = Math.floor(diff / 60_000);
    if (mins < 60) return `${Math.max(0, mins)}m`;
    const hours = Math.floor(mins / 60);
    if (hours < 24) return `${hours}h`;
    return `${Math.floor(hours / 24)}d`;
  }

  trackByCurrency(_: number, item: CurrencyTotal): string {
    return item.currency;
  }

  attainmentGaugeValue(): number {
    const pct = this.store.periodBand()?.avgQuotaAttainmentPercent;
    if (pct === null || pct === undefined) return 0;
    return pct / 100;
  }
}
