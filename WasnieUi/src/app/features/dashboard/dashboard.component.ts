import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { DecimalPipe, LowerCasePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { RefreshOnEnterDirective } from '../../shared/directives/refresh-on-enter.directive';
import { CurrencyFormatPipe } from '../../shared/pipes/currency-format.pipe';
import { HasPermissionPipe } from '../../shared/pipes/has-permission.pipe';
import { DashboardStore } from './store/dashboard.store';
import { CurrencyTotal, DashboardTrendPoint, UnprocessablePendingItem, DriftAlertItem, DealLostAlertItem, AmbiguousAttributionPayee } from './models/dashboard.models';
import { TransactionsApiService } from '../transactions/services/transactions.api.service';
import { ProfileService } from '../profile/services/profile.service';
import { AuthService } from '../../core/services/auth.service';
import { TerminatedAccountsStore } from '../ledger/state/terminated-accounts.store';
import { ToastService } from '../../shared/services/toast.service';
import { extractApiError } from '../../shared/utils/api-error';
import {
  WsCardComponent,
  WsBadgeComponent,
  WsPageLayoutComponent,
  WsSelectComponent,
  WsStatCardComponent,
  WsGaugeComponent,
  WsBarChartComponent,
  WsSparklineChartComponent,
  WsHBarChartComponent,
  WsButtonComponent,
  WsConfirmationModalComponent,
  type SelectOption,
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
    ReactiveFormsModule,
    WsSelectComponent,
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
  private readonly profile = inject(ProfileService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

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

  private readonly destroyRef = inject(DestroyRef);

  /**
   * The period filter's control. WsSelect is a ControlValueAccessor — it has no [value]/(valueChange)
   * pair — so the binding goes through a FormControl, which is how every other WsSelect in the app is
   * driven. Seeded from the store so the control shows the period already in effect.
   */
  readonly periodControl = new FormControl<string>(this.store.period(), { nonNullable: true });

  /**
   * The person the header greets. The name is NOT on `/auth/me` (CurrentUser carries identity and
   * permissions, not display fields), so it comes from the profile endpoint that already serves it.
   * Empty until it arrives, and empty forever for a user who never filled it in — hence the email
   * fallback below rather than a greeting addressed to nobody.
   */
  private readonly profileFirstName = signal('');

  /**
   * First name, or the local part of the email capitalised when there is no name on file. Never a
   * bare "there"/"user": the email prefix is at least the person's own handle.
   */
  readonly greetingName = computed(() => {
    const first = this.profileFirstName().trim();
    if (first) return first;
    const local = (this.auth.currentUser()?.email ?? '').split('@')[0];
    if (!local) return '';
    return local.charAt(0).toUpperCase() + local.slice(1);
  });

  /**
   * Morning / afternoon / evening by the BROWSER's clock — the greeting is about where the user is
   * sitting, not where the server runs. Boundaries: [0,12) morning, [12,18) afternoon, [18,24) evening.
   */
  readonly greetingKey = computed(() => {
    const hour = this.now().getHours();
    if (hour < 12) return 'DASHBOARD.GREETING_MORNING';
    if (hour < 18) return 'DASHBOARD.GREETING_AFTERNOON';
    return 'DASHBOARD.GREETING_EVENING';
  });

  /**
   * The name clause, already punctuated, interpolated into the greeting string. It carries the comma
   * so the three greeting keys stay single strings that still read correctly when there is no name at
   * all ("Good morning" rather than "Good morning, ").
   */
  readonly greetingNamePart = computed(() => {
    const name = this.greetingName();
    return name ? `, ${name}` : '';
  });

  /** Re-read once per minute so a session open across noon or 6pm eventually corrects itself. */
  private readonly now = signal(new Date());

  constructor() {
    void this.terminated.load();

    this.profile.getProfile()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: p => this.profileFirstName.set(p.firstName ?? ''),
        // A missing name is not worth a toast — the email fallback covers it.
        error: () => {},
      });

    const clock = setInterval(() => this.now.set(new Date()), 60_000);
    this.destroyRef.onDestroy(() => clearInterval(clock));

    // Selecting an option routes to the SAME handler the segmented control called. No period logic
    // moved into this component: the store still owns the period and the reload.
    this.periodControl.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(value => this.onPeriodChange(value));
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

  /**
   * THE ONE definition of a payouts deep-link. The card and both chart bars go through it, so a card and
   * the bar beside it can never point at different things.
   *
   * The payouts card is CASH FLOW: money that actually left in a window. So it filters the list the same
   * way the card sums — by PAYMENT date (payFrom/payTo) and Status=Paid — not by the compensation period
   * (pFrom/pTo), which is a different question and would open a list whose total does not match the
   * number that was clicked. With these params the list adds up to the figure shown, to the cent.
   */
  payoutsLinkParamsFor(from: string | null, to: string | null): Record<string, string> {
    const p: Record<string, string> = { status: 'Paid' };
    if (from) p['payFrom'] = from;
    if (to) p['payTo'] = to;
    return p;
  }

  readonly payoutsLinkParams = computed<Record<string, string>>(() => {
    const key = this.store.period();
    const { from, to } = this._periodDates(key);
    return { period: key, ...this.payoutsLinkParamsFor(from, to) };
  });

  /**
   * Drill-down: clicking a bar opens the payouts that make up exactly that bar — the prior bar its own
   * window, the current bar the period on screen. Windows come from the trend band (computed by
   * PeriodHelper), never recomputed here, so the bar and the list cannot disagree.
   */
  onTrendBarClick(point: BarChartPoint): void {
    const band = this.store.trendBand();
    if (!band) return;

    const [from, to] = point.isCurrent
      ? [band.currentFrom, band.currentTo]
      : [band.priorFrom, band.priorTo];

    if (!from && !to) return;   // an unbounded window would open the whole table, not a drill-down

    void this.router.navigate(['/payouts'], {
      queryParams: this.payoutsLinkParamsFor(from, to),
    });
  }

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
   * Mirrors PeriodHelper.ComputeDateRange on the backend — these values only build the deep links to
   * the list screens; the dashboard's own figures come from the backend, which computes the same
   * ranges. If one side changes, both must (PeriodHelperQuarterAndYearTests pins the backend).
   *
   *   this-month   : first of month      → today
   *   last-month   : first of prev month → last day of prev month
   *   this-quarter : first of quarter    → today (quarter TO DATE)
   *   last-quarter : previous quarter, in full
   *   ytd          : Jan 1               → today
   *   last-year    : previous calendar year, in full
   *
   * Quarters are calendar quarters (Q1 Jan–Mar … Q4 Oct–Dec); Wasnie has no fiscal-year concept.
   */
  _periodDates(key: string): { from: string | null; to: string | null } {
    const today = new Date();
    const yyyy = today.getFullYear();
    const mm = String(today.getMonth() + 1).padStart(2, '0');
    const dd = String(today.getDate()).padStart(2, '0');
    const todayStr = `${yyyy}-${mm}-${dd}`;

    // Local-time formatting on purpose: toISOString() converts to UTC and shifts the date by a day for
    // anyone west of Greenwich, which would silently pick the wrong month or quarter.
    const fmt = (d: Date): string =>
      `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

    // Month index (0-11) of the first month of the quarter containing `today`.
    const quarterStartMonth = Math.floor(today.getMonth() / 3) * 3;

    switch (key) {
      case 'this-month':
        return { from: `${yyyy}-${mm}-01`, to: todayStr };
      case 'last-month': {
        const first = new Date(yyyy, today.getMonth() - 1, 1);
        const last = new Date(yyyy, today.getMonth(), 0);
        return { from: fmt(first), to: fmt(last) };
      }
      case 'this-quarter':
        return { from: fmt(new Date(yyyy, quarterStartMonth, 1)), to: todayStr };
      case 'last-quarter': {
        // Day 0 of a month is the last day of the previous one, and the Date constructor rolls a
        // negative month back into the previous year — so Q1 correctly yields Q4 of last year.
        const first = new Date(yyyy, quarterStartMonth - 3, 1);
        const last = new Date(yyyy, quarterStartMonth, 0);
        return { from: fmt(first), to: fmt(last) };
      }
      case 'ytd':
        return { from: `${yyyy}-01-01`, to: todayStr };
      case 'last-year':
        return { from: `${yyyy - 1}-01-01`, to: `${yyyy - 1}-12-31` };
      default:
        // Unknown keys (including the retired 'all-time', which may still sit in a bookmarked URL)
        // degrade to no date filter rather than throwing.
        return { from: null, to: null };
    }
  }

  /**
   * "All time" was removed deliberately: as a quick filter it is an analytics anti-pattern (it blends
   * years run under different plans into one number) and an unbounded scan that degrades as data grows.
   * The default is 'this-month' (see DashboardStore), so removing it does not orphan the initial state.
   */
  readonly periodOptions: SelectOption[] = [
    { value: 'this-month', label: 'DASHBOARD.PERIOD_THIS_MONTH' },
    { value: 'last-month', label: 'DASHBOARD.PERIOD_LAST_MONTH' },
    { value: 'this-quarter', label: 'DASHBOARD.PERIOD_THIS_QUARTER' },
    { value: 'last-quarter', label: 'DASHBOARD.PERIOD_LAST_QUARTER' },
    { value: 'ytd', label: 'DASHBOARD.PERIOD_YTD' },
    { value: 'last-year', label: 'DASHBOARD.PERIOD_LAST_YEAR' },
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

  /**
   * Arrow for a CLOSED period's change. "pacing" never reaches here — the template routes running
   * periods to the progress bar — but it is accepted and drawn flat so a stray call can never render a
   * running period as a rise or a fall.
   */
  trendIcon(direction: DashboardTrendPoint['direction']): string {
    return direction === 'up' || direction === 'down' ? 'trend-up' : 'trend-neutral';
  }

  /** True when the change% is either null (prior=0) or absurdly large (near-zero prior). */
  trendIsNoBase(point: DashboardTrendPoint): boolean {
    return point.changePercent === null || Math.abs(point.changePercent) > 500;
  }

  // ── Pacing (running periods) ───────────────────────────────────────────────
  // A running period is NOT shown as a change percentage. Five days of August against the whole of July
  // is -89.9%, and a red collapse arrow every first of the month is exactly what these helpers exist to
  // prevent. Instead: how far the period has got against the previous period's total.

  /** True when there is a baseline to pace against (previous period total > 0). */
  hasPacingBase(point: DashboardTrendPoint): boolean {
    return point.pacingPercent !== null && point.pacingPercent !== undefined;
  }

  /** Whole-number pacing percentage, e.g. 10 for "10% of last month's total". */
  pacingPercent(point: DashboardTrendPoint): number {
    return Math.round(point.pacingPercent ?? 0);
  }

  /** The baseline has been matched or beaten — rendered as a positive outcome, never as a shortfall. */
  pacingExceeded(point: DashboardTrendPoint): boolean {
    return (point.pacingPercent ?? 0) >= 100;
  }

  // ── One badge, one footer, both states ─────────────────────────────────────
  // The card is a single skeleton: same box, same chart, same axis, same badge geometry, same footer.
  // Only text and colour change between trend and progress, and these helpers are where that difference
  // lives — so the two states cannot drift apart structurally when either one is edited.

  private get isPacing(): boolean {
    return this.store.trendBand()?.isPacing ?? false;
  }

  /** Green badge: a real rise for a closed period, or a beaten baseline while pacing. */
  badgeIsPositive(point: DashboardTrendPoint): boolean {
    return this.isPacing
      ? this.hasPacingBase(point) && this.pacingExceeded(point)
      : point.direction === 'up' && !this.trendIsNoBase(point);
  }

  /**
   * Red badge — only ever for a CLOSED period. A running period can never reach this: it has not
   * finished, so there is nothing to be down against.
   */
  badgeIsNegative(point: DashboardTrendPoint): boolean {
    return !this.isPacing && point.direction === 'down' && !this.trendIsNoBase(point);
  }

  /** The arrow belongs to a change percentage; progress has no direction to point in. */
  badgeShowsArrow(point: DashboardTrendPoint): boolean {
    return !this.isPacing && !this.trendIsNoBase(point);
  }

  /** Translation key for the badge text. */
  badgeText(point: DashboardTrendPoint): string {
    if (this.isPacing) {
      return this.hasPacingBase(point) ? 'DASHBOARD.PACING_PILL' : 'DASHBOARD.TREND_NO_BASE';
    }
    // Closed periods with no usable base fall back to the same "New" label, so the badge never shows a
    // meaningless percentage. trendChangeFormatted is already pre-formatted, hence the literal key.
    return this.trendIsNoBase(point) ? 'DASHBOARD.TREND_NO_BASE' : this.trendChangeFormatted(point);
  }

  /** Interpolation params for the badge key (empty when the "key" is already formatted text). */
  badgeParams(point: DashboardTrendPoint): Record<string, unknown> {
    return this.isPacing && this.hasPacingBase(point)
      ? { percent: this.pacingPercent(point) }
      : {};
  }

  /** Footer wording: "Prior: June 2026" for a closed period, "July 2026 total" for a baseline. */
  footerLabel(): string {
    return this.isPacing ? 'DASHBOARD.PACING_BASELINE' : 'DASHBOARD.TREND_PRIOR';
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
