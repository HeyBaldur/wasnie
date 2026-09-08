import { Component, DestroyRef, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { bindFiltersToUrl } from '../../../shared/state/bind-filters-to-url';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PayeesStore } from '../state/payees.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { QuotaStatusVariantPipe, QuotaStatusLabelPipe, QuotaPeriodExpiredPipe } from '../../../shared/pipes/quota-status.pipe';
import { currentMonthRange, type DashboardRange } from '../../dashboard/store/dashboard.store';
import { CurrencyTotal } from '../../dashboard/models/dashboard.models';
import { PayeesApiService } from '../services/payees.api.service';
import { QuotaMeasurementType, QuotaSummary } from '../../quotas/models/quota.model';
import { PayeeDashboard, SalesTrendPoint } from '../models/payee-dashboard.model';
import { Payee, PayeeStatus } from '../models/payee.model';
import { PayeeFormComponent } from '../form/payee-form.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { WsLoadMoreDirective } from '../../../shared/directives/ws-load-more.directive';
import { PagedResult } from '../../../shared/models/pagination.models';
import { Assignment } from '../../assignments/models/assignment.model';
import { CreditListItem } from '../../credits/models/credit.model';
import {
  WsBadgeComponent,
  WsButtonComponent,
  WsCardComponent,
  WsDatePickerComponent,
  WsModalComponent,
  WsConfirmationModalComponent,
  WsTableEmptyComponent,
  WsGaugeComponent,
  WsBarChartComponent,
  WsDateRangePickerComponent,
  WsTooltipDirective,
  type BarChartPoint,
  type DateRange,
  type BadgeVariant,
} from '../../../shared/ui';
import { PayeeLedgerPanelComponent } from '../../ledger/panel/payee-ledger-panel.component';

type Tab = 'overview' | 'profile' | 'ledger';
/** The blocks of the Overview that can be collapsed. */
type Block = 'attainment' | 'trend' | 'quotas' | 'assignments' | 'credits';

@Component({
  selector: 'app-payee-detail',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    ReactiveFormsModule,
    TranslateModule,
    DecimalPipe,
    DateFormatPipe,
    CurrencyFormatPipe,
    QuotaStatusVariantPipe,
    QuotaStatusLabelPipe,
    QuotaPeriodExpiredPipe,
    PayeeFormComponent,
    HasPermissionDirective,
    WsLoadMoreDirective,
    WsBadgeComponent,
    WsButtonComponent,
    WsCardComponent,
    WsDatePickerComponent,
    WsModalComponent,
    WsConfirmationModalComponent,
    WsTableEmptyComponent,
    WsGaugeComponent,
    WsBarChartComponent,
    WsDateRangePickerComponent,
    WsTooltipDirective,
    PayeeLedgerPanelComponent,
  ],
  templateUrl: './payee-detail.component.html',
  styleUrl: './payee-detail.component.scss',
})
export class PayeeDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);
  private readonly payeesApi = inject(PayeesApiService);
  readonly store = inject(PayeesStore);
  private readonly toast = inject(ToastService);

  readonly PayeeStatus = PayeeStatus;
  readonly QuotaMeasurementType = QuotaMeasurementType;

  // A getter, not a captured field: Angular REUSES this component when navigating between two
  // payees (e.g. clicking the manager link), so a value captured at construction would go stale
  // and every subsequent request would still target the previous payee.
  get payeeId(): string { return this.route.snapshot.paramMap.get('payeeId')!; }
  // Stable timestamp for the component's lifetime — prevents NG0100 from
  // computePacing() returning slightly-different floats on each CD pass.
  private readonly _nowMs = Date.now();
  readonly activeTab = signal<Tab>('overview');
  /**
   * The range that governs this page's report data. Whole current month by default — same rule and
   * same default as the dashboard (KAN-62), so the two screens agree about what "this month" is.
   */
  readonly range = signal<DashboardRange>(currentMonthRange());

  /** Seeded from the range so the picker opens showing what is already applied. */
  readonly rangeControl = new FormControl<DateRange>(
    { start: this.range().from, end: this.range().to },
    { nonNullable: true }
  );

  /**
   * Which Overview blocks are open. ALL START CLOSED, and the state is not persisted — reloading
   * returns to that, exactly like the dashboard's attention panel.
   *
   * A Set rather than five booleans: five signals would need five toggles and five template branches,
   * and nothing would stop a sixth block from being added with its own slightly different wiring.
   */
  readonly expandedBlocks = signal<ReadonlySet<Block>>(new Set());

  isBlockOpen(block: Block): boolean {
    return this.expandedBlocks().has(block);
  }

  toggleBlock(block: Block): void {
    this.expandedBlocks.update(open => {
      const next = new Set(open);
      if (!next.delete(block)) next.add(block);
      return next;
    });
  }

  // ── This payee's commission cards ─────────────────────────────────────────
  //
  // Declared once and rendered by one loop, exactly like the dashboard's: the requirement is that the
  // three are structurally identical, and three hand-written blocks satisfy that on the day they are
  // written and drift the first time one of them is touched.

  readonly commissionCards: ReadonlyArray<{
    key: 'total' | 'paid' | 'unpaid';
    titleKey: string;
    descKey: string;
    /** The settlement filter the credits screen must apply for its rows to add up to this card. */
    settlement: 'Payable' | 'Paid' | 'Unpaid';
  }> = [
    { key: 'total', titleKey: 'DASHBOARD.COMMISSIONS_TOTAL', descKey: 'DASHBOARD.COMMISSIONS_TOTAL_DESC', settlement: 'Payable' },
    { key: 'paid', titleKey: 'DASHBOARD.COMMISSIONS_PAID', descKey: 'DASHBOARD.COMMISSIONS_PAID_DESC', settlement: 'Paid' },
    { key: 'unpaid', titleKey: 'DASHBOARD.COMMISSIONS_UNPAID', descKey: 'DASHBOARD.COMMISSIONS_UNPAID_DESC', settlement: 'Unpaid' },
  ];

  commissionTotals(key: 'total' | 'paid' | 'unpaid'): CurrencyTotal[] {
    const band = this.dashboard()?.commissionsBand;
    if (!band) return [];
    if (key === 'paid') return band.paidByCurrency;
    if (key === 'unpaid') return band.unpaidByCurrency;
    return band.totalByCurrency;
  }

  /**
   * The three cards read one currency at a time and it must be the SAME one — a Total in euros beside
   * a Paid in zlotys would not add up and nothing on screen would say why. Taken from the Total card,
   * the only list guaranteed to contain every currency present in the other two.
   */
  readonly commissionsCurrency = computed<string | null>(() =>
    this.dashboard()?.commissionsBand?.totalByCurrency?.[0]?.currency ?? null
  );

  amountFor(totals: CurrencyTotal[] | undefined, currency: string | null): number {
    if (!currency) return 0;
    return totals?.find(t => t.currency === currency)?.amount ?? 0;
  }

  /** Currencies beyond the primary one, so a multi-currency payee still sees the rest. */
  secondaryCurrencies(totals: CurrencyTotal[] | undefined): CurrencyTotal[] {
    return (totals ?? []).slice(1);
  }

  readonly hasClosedCommissions = computed(() =>
    (this.dashboard()?.commissionsBand?.closedTotalByCurrency?.length ?? 0) > 0
  );

  /**
   * Deep link for a commission card: this payee's credits, same window and same date field the card
   * sums on (allocation), so the list adds up to the figure that was clicked.
   */
  commissionsLinkParams(settlement: 'Payable' | 'Paid' | 'Unpaid'): Record<string, string> {
    const { from, to } = this.range();
    return { payeeIds: this.payeeId, allocFrom: from, allocTo: to, settlement };
  }

  /**
   * Compact notation for monetary values. A blank currency is a real state — a range with no money has
   * no currency to name — and Intl throws a RangeError on an empty code, so it degrades instead.
   */
  fmtCompact(amount: number, currency: string): string {
    const options: Intl.NumberFormatOptions = currency
      ? { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 2 }
      : { notation: 'compact', maximumFractionDigits: 2 };
    return new Intl.NumberFormat('en-US', options).format(amount);
  }

  readonly invalidQuotaCount = computed(() =>
    (this.dashboard()?.attainmentItems ?? []).filter(a => !a.isCurrencyValid).length
  );

  readonly editModalOpen = signal(false);
  readonly terminateModalOpen = signal(false);
  readonly deactivateModalOpen = signal(false);
  readonly saving = signal(false);
  readonly deactivateSaving = signal(false);

  // Dashboard (gauges + trend)
  readonly dashboard = signal<PayeeDashboard | null>(null);
  readonly dashboardLoading = signal(false);

  // Assignments virtual scroll
  readonly assignments = signal<Assignment[]>([]);
  readonly assignmentsPage = signal(1);
  readonly assignmentsTotal = signal(0);
  readonly assignmentsLoading = signal(false);
  readonly assignmentsHasMore = computed(() => this.assignments().length < this.assignmentsTotal());

  // Quotas virtual scroll
  readonly quotas = signal<QuotaSummary[]>([]);
  readonly quotasPage = signal(1);
  readonly quotasTotal = signal(0);
  readonly quotasLoading = signal(false);
  readonly quotasHasMore = computed(() => this.quotas().length < this.quotasTotal());

  // Credits virtual scroll
  readonly credits = signal<CreditListItem[]>([]);
  readonly creditsPage = signal(1);
  readonly creditsTotal = signal(0);
  readonly creditsLoading = signal(false);
  readonly creditsHasMore = computed(() => this.credits().length < this.creditsTotal());

  readonly terminateForm = this.fb.nonNullable.group({
    terminationDate: ['', Validators.required],
  });

  ngOnInit(): void {
    // Two subscriptions, two different jobs — and the ORDER matters.
    //
    // This one owns the query params (?tab=, ?from=, ?to=), which a snapshot read in ngOnInit would
    // have frozen at first mount. It goes FIRST so the range is already correct when the initial load
    // runs below: bound the other way round, arriving at ?from=…&to=… would fetch the default month,
    // then immediately refetch — two round trips and a visible flash of the wrong numbers.
    //
    // Both handlers act ONLY on a real change, which is what makes this safe next to onRangeChange():
    // onRangeChange writes the URL with router.navigate, which the router DOES observe, so its own
    // navigation echoes back here. Without the equality check the echo would re-run resetAndLoad and
    // refetch the whole page a second time on every range change.
    bindFiltersToUrl(this.route, this.destroyRef, {
      apply: qp => this._applyUrlState(qp['tab'] as Tab | undefined),
      reset: () => this._applyUrlState(undefined),
    });

    // The picker is a ControlValueAccessor, so its value arrives through the FormControl rather than
    // an output — the same wiring the dashboard uses.
    this.rangeControl.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value: DateRange) => this.onRangeChange(value));

    // paramMap emits immediately, so this covers the first render AND every later navigation between
    // payees. Without it, clicking the manager link would change the URL but leave the
    // previously-loaded payee on screen, because Angular reuses the component.
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.initForCurrentPayee());
  }

  /** False until the first load has been kicked off, so the initial URL read does not double-fetch. */
  private _loadStarted = false;

  /** Applies ?tab= / ?from= / ?to= from the URL, doing nothing when they already match. */
  private _applyUrlState(requested: Tab | undefined): void {
    const tabs: Tab[] = ['overview', 'profile', 'ledger'];
    const tab: Tab = requested && tabs.includes(requested) ? requested : 'overview';
    if (tab !== this.activeTab()) this.activeTab.set(tab);

    // ?from=&to= replace the retired ?period=. Both must be present and ordered; anything else is a
    // stale or hand-edited link and degrades to the default month rather than to a broken window.
    const from = this.route.snapshot.queryParamMap.get('from');
    const to = this.route.snapshot.queryParamMap.get('to');
    const fallback = currentMonthRange();
    const next: DashboardRange = from && to && from <= to ? { from, to } : fallback;

    if (next.from !== this.range().from || next.to !== this.range().to) {
      this.range.set(next);
      this.rangeControl.setValue({ start: next.from, end: next.to }, { emitEvent: false });
      // Before the first load there is nothing to reset — initForCurrentPayee is about to run and
      // will fetch with the range just set. Reloading here would only duplicate that request.
      if (this._loadStarted) this.resetAndLoad();
    }
  }

  /// Mirrors the original init sequence (loadPayee + loadOverview, which loads the list cards in
  /// its finally) and additionally clears the previous payee's data so nothing bleeds across.
  private initForCurrentPayee(): void {
    // Honour ?tab= when it names a real tab — the same convention plan-detail already uses. It is
    // what lets a deep link land where it promised: the terminated-accounts queue sends finance
    // straight to the clawback tab to close an account, not to a page they must navigate again.
    const requested = this.route.snapshot.queryParamMap.get('tab') as Tab | null;
    const tabs: Tab[] = ['overview', 'profile', 'ledger'];
    this.activeTab.set(requested && tabs.includes(requested) ? requested : 'overview');
    this.dashboard.set(null);
    this.assignments.set([]); this.assignmentsPage.set(1); this.assignmentsTotal.set(0);
    this.quotas.set([]); this.quotasPage.set(1); this.quotasTotal.set(0);
    this.credits.set([]); this.creditsPage.set(1); this.creditsTotal.set(0);

    this._loadStarted = true;
    this.store.loadPayee(this.payeeId);
    this.loadOverview();
  }

  // ── Tab switching ────────────────────────────────────────────────────────

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  // ── Period filter ─────────────────────────────────────────────────────────

  /**
   * The picker orders the two ends itself, so a backwards range cannot leave the control; the guard
   * here is the second door and the server refuses one as a third.
   */
  onRangeChange(value: DateRange): void {
    if (!value?.start || !value?.end || value.end < value.start) return;
    if (value.start === this.range().from && value.end === this.range().to) return;

    this.range.set({ from: value.start, to: value.end });
    this.router.navigate([], {
      queryParams: { from: value.start, to: value.end },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.resetAndLoad();
  }

  private resetAndLoad(): void {
    this.dashboard.set(null);
    this.assignments.set([]); this.assignmentsPage.set(1); this.assignmentsTotal.set(0);
    this.quotas.set([]); this.quotasPage.set(1); this.quotasTotal.set(0);
    this.credits.set([]); this.creditsPage.set(1); this.creditsTotal.set(0);
    this.loadOverview();
    this.loadMoreAssignments();
    this.loadMoreQuotas();
    this.loadMoreCredits();
  }

  // ── Dashboard (gauges + trend) ────────────────────────────────────────────

  private async loadOverview(): Promise<void> {
    this.dashboardLoading.set(true);
    try {
      const result = await firstValueFrom(this.payeesApi.getPayeeDashboard(this.payeeId, this.range().from, this.range().to));
      this.dashboard.set(result);
    } catch {
      this.dashboard.set({
        from: this.range().from,
        to: this.range().to,
        commissionsBand: {
          totalByCurrency: [], paidByCurrency: [], unpaidByCurrency: [], closedTotalByCurrency: [],
        },
        attainmentItems: [], salesTrend: [], recentQuotas: [], recentAssignments: [],
      });
    } finally {
      this.dashboardLoading.set(false);
      // Load list cards after dashboard
      this.loadMoreAssignments();
      this.loadMoreQuotas();
      this.loadMoreCredits();
    }
  }

  // ── Virtual scroll list cards ─────────────────────────────────────────────

  async loadMoreAssignments(): Promise<void> {
    if (this.assignmentsLoading() || (!this.assignmentsHasMore() && this.assignmentsPage() > 1)) return;
    this.assignmentsLoading.set(true);
    try {
      const result = await firstValueFrom(
        this.payeesApi.getPayeeAssignments(this.payeeId, {
          page: this.assignmentsPage(),
          pageSize: 10,
          sortBy: 'effectivestart',
          sortOrder: 'desc',
          dateFrom: this.range().from,
          dateTo: this.range().to,
        })
      );
      this.assignments.update(prev => [...prev, ...result.items]);
      this.assignmentsTotal.set(result.totalCount);
      this.assignmentsPage.update(p => p + 1);
    } catch { /* silent */ } finally {
      this.assignmentsLoading.set(false);
    }
  }

  async loadMoreQuotas(): Promise<void> {
    if (this.quotasLoading() || (!this.quotasHasMore() && this.quotasPage() > 1)) return;
    this.quotasLoading.set(true);
    try {
      const result = await firstValueFrom(
        this.payeesApi.getPayeeQuotas(this.payeeId, {
          page: this.quotasPage(),
          pageSize: 10,
          sortBy: 'periodstart',
          sortOrder: 'desc',
          dateFrom: this.range().from,
          dateTo: this.range().to,
        })
      );
      this.quotas.update(prev => [...prev, ...result.items]);
      this.quotasTotal.set(result.totalCount);
      this.quotasPage.update(p => p + 1);
    } catch { /* silent */ } finally {
      this.quotasLoading.set(false);
    }
  }

  async loadMoreCredits(): Promise<void> {
    if (this.creditsLoading() || (!this.creditsHasMore() && this.creditsPage() > 1)) return;
    this.creditsLoading.set(true);
    try {
      const result = await firstValueFrom(
        this.payeesApi.getPayeeCredits(this.payeeId, this.creditsPage(), this.range().from, this.range().to)
      );
      this.credits.update(prev => [...prev, ...result.items]);
      this.creditsTotal.set(result.totalCount);
      this.creditsPage.update(p => p + 1);
    } catch { /* silent */ } finally {
      this.creditsLoading.set(false);
    }
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  trendBarPoints(trend: SalesTrendPoint[]): BarChartPoint[] {
    if (!trend.length) return [];
    const today = new Date();
    const currentYear = today.getFullYear();
    const currentMonth = today.getMonth() + 1; // 1-based

    // V1: show only the dominant currency (highest total across all months).
    // This avoids mixing EUR + PLN amounts in the same bar axis.
    const totalByCurrency = new Map<string, number>();
    for (const p of trend) {
      totalByCurrency.set(p.currency, (totalByCurrency.get(p.currency) ?? 0) + p.amount);
    }
    const dominantCurrency = [...totalByCurrency.entries()]
      .reduce((a, b) => a[1] >= b[1] ? a : b)[0];

    const byMonth = new Map<string, SalesTrendPoint>();
    for (const p of trend) {
      if (p.currency === dominantCurrency) byMonth.set(`${p.year}-${p.month}`, p);
    }

    return [...byMonth.values()]
      .sort((a, b) => a.year !== b.year ? a.year - b.year : a.month - b.month)
      .map(p => ({
        label: p.monthLabel,
        value: p.amount,
        currency: p.currency,
        isCurrent: p.year === currentYear && p.month === currentMonth,
      }));
  }

  /** Compute pacing fraction for a currently-active quota period. */
  computePacing(periodStart: string, periodEnd: string): number | null {
    const start = new Date(periodStart).getTime();
    const end = new Date(periodEnd).getTime();
    if (this._nowMs < start || this._nowMs > end) return null;
    return Math.min(1, Math.max(0, (this._nowMs - start) / (end - start)));
  }

  gaugeColorClass(value: number): string {
    const pct = value * 100;
    if (pct >= 100) return 'bento-bar--blue';
    if (pct >= 80)  return 'bento-bar--green';
    if (pct >= 50)  return 'bento-bar--amber';
    return 'bento-bar--red';
  }

  // ── Profile + status helpers ──────────────────────────────────────────────

  openEdit(): void { this.editModalOpen.set(true); }
  onEditSaved(_payee: Payee): void { this.editModalOpen.set(false); }

  async onMarkActive(): Promise<void> {
    try { await this.store.markAsActive(this.payeeId); this.toast.show('PAYEES.TOAST_MARKED_ACTIVE', 'success'); }
    catch (err) { this.toast.show(extractApiError(err), 'error'); }
  }

  async onMarkOnLeave(): Promise<void> {
    try { await this.store.markAsOnLeave(this.payeeId); this.toast.show('PAYEES.TOAST_MARKED_ON_LEAVE', 'success'); }
    catch (err) { this.toast.show(extractApiError(err), 'error'); }
  }

  openDeactivate(): void { this.deactivateModalOpen.set(true); }

  async onConfirmDeactivate(): Promise<void> {
    this.deactivateSaving.set(true);
    try {
      await this.store.deactivate(this.payeeId);
      this.toast.show('PAYEES.TOAST_DEACTIVATED', 'success');
      this.deactivateModalOpen.set(false);
    } catch (err) { this.toast.show(extractApiError(err), 'error'); }
    finally { this.deactivateSaving.set(false); }
  }

  async onActivate(): Promise<void> {
    try { await this.store.activate(this.payeeId); this.toast.show('PAYEES.TOAST_ACTIVATED', 'success'); }
    catch (err) { this.toast.show(extractApiError(err), 'error'); }
  }

  openTerminate(): void {
    const today = new Date();
    this.terminateForm.reset({ terminationDate: today.toISOString().slice(0, 10) });
    this.terminateModalOpen.set(true);
  }

  async onConfirmTerminate(): Promise<void> {
    if (this.terminateForm.invalid) { this.terminateForm.markAllAsTouched(); return; }
    const { terminationDate } = this.terminateForm.getRawValue();
    this.saving.set(true);
    try {
      await this.store.markAsTerminated(this.payeeId, terminationDate);
      this.toast.show('PAYEES.TOAST_TERMINATED', 'success');
      this.terminateModalOpen.set(false);
    } catch (err) { this.toast.show(extractApiError(err), 'error'); }
    finally { this.saving.set(false); }
  }

  payeeStatusVariant(status: PayeeStatus): BadgeVariant {
    switch (status) {
      case PayeeStatus.Active: return 'success';
      case PayeeStatus.OnLeave: return 'warning';
      case PayeeStatus.Terminated: return 'neutral';
    }
  }

  payeeStatusKey(status: PayeeStatus): string {
    switch (status) {
      case PayeeStatus.Active: return 'PAYEES.STATUS_ACTIVE';
      case PayeeStatus.OnLeave: return 'PAYEES.STATUS_ON_LEAVE';
      case PayeeStatus.Terminated: return 'PAYEES.STATUS_TERMINATED';
    }
  }

  // Temporal chip: derives state from entity period vs today.
  // If isCurrencyValid is false, returns the warning/invalid state regardless.
  //
  // `status` takes precedence when supplied: a deactivated assignment is NOT "In Progress" no matter
  // what its dates say. Without this the period alone decided the chip, so a deactivated assignment
  // whose period covered today rendered green and current — the exact bug this fixes. Kept as a guard
  // even though the list is now filtered server-side, so it cannot come back if these rows are ever
  // shown again (e.g. a future "show all" toggle).
  temporalVariant(start: string, end: string, isCurrencyValid = true, status?: string): BadgeVariant {
    if (status === 'Deactivated') return 'neutral';
    if (!isCurrencyValid) return 'warning';
    const today = new Date(); today.setHours(0, 0, 0, 0);
    if (new Date(end) < today)   return 'neutral';  // Closed
    if (new Date(start) > today) return 'info';     // Upcoming
    return 'success';                               // In Progress
  }

  temporalKey(start: string, end: string, isCurrencyValid = true, status?: string): string {
    if (status === 'Deactivated') return 'ASSIGNMENTS.STATUS_DEACTIVATED';
    if (!isCurrencyValid) return 'DASHBOARD.CHIP_INVALID';
    const today = new Date(); today.setHours(0, 0, 0, 0);
    if (new Date(end) < today)   return 'DASHBOARD.CHIP_CLOSED';
    if (new Date(start) > today) return 'DASHBOARD.CHIP_UPCOMING';
    return 'DASHBOARD.CHIP_IN_PROGRESS';
  }

  currencyMismatchTooltip(quotaCurrency: string, planCurrency: string): string {
    return `Currency mismatch: quota is ${quotaCurrency} but plan is ${planCurrency}. Close and recreate this quota to fix.`;
  }

  hasTerminateError(field: string, error: string): boolean {
    const ctrl = this.terminateForm.get(field);
    return !!(ctrl?.touched && ctrl.hasError(error));
  }
}
