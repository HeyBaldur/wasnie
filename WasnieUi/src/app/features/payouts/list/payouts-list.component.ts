import {
  Component, computed, inject, OnInit, signal, DestroyRef,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { distinctUntilChanged, map } from 'rxjs/operators';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PayoutsStore, PayoutFilter, EMPTY_PAYOUT_FILTER } from '../state/payouts.store';
import { PayoutStatus } from '../models/payout.model';
import { parseBulkMarkPaidError } from './bulk-mark-paid-error';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsSelectComponent,
  WsDatePickerComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsPaginationComponent,
  WsModalComponent,
  WsSegmentedControlComponent,
  type BadgeVariant,
  type SelectOption,
  type SegOption,
} from '../../../shared/ui';

type PeriodKey = 'this-month' | 'last-month' | 'ytd' | 'all-time';

@Component({
  selector: 'app-payouts-list',
  standalone: true,
  imports: [
    RouterLink,
    AppShellComponent, RefreshOnEnterDirective, ReactiveFormsModule, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    WsButtonComponent, WsBadgeComponent, WsCardComponent,
    WsSelectComponent, WsDatePickerComponent, WsSegmentedControlComponent,
    WsPageLayoutComponent, WsTableComponent, WsTableEmptyComponent,
    WsPaginationComponent, WsModalComponent,
  ],
  templateUrl: './payouts-list.component.html',
  styleUrl: './payouts-list.component.scss',
})
export class PayoutsListComponent implements OnInit {
  readonly store = inject(PayoutsStore);
  private readonly api = inject(PayoutsApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly plansApi = inject(PlansApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly currentUser = inject(CurrentUserService);

  readonly filterOpen = signal(false);
  readonly bulkApproveConfirmOpen = signal(false);
  readonly bulkApproving = signal(false);
  readonly bulkMarkPaidConfirmOpen = signal(false);
  readonly bulkMarkPaiding = signal(false);
  readonly bulkMarkPaidErrors = signal<string[]>([]);

  /**
   * The same refusals, taken apart so the template can lay them out.
   *
   * The server sends one unbroken line of comma-separated GUIDs per payout; see
   * `bulk-mark-paid-error.ts` for why it is parsed here and what happens to a line that does not
   * match — it renders exactly as it does today, never blank.
   */
  readonly bulkMarkPaidErrorBlocks = computed(() =>
    this.bulkMarkPaidErrors().map(parseBulkMarkPaidError)
  );

  /**
   * Whether each identifier in a refusal is worth turning into a link.
   *
   * ★★ A LINK, OR PLAIN TEXT — NEVER A LINK THAT LEADS TO A REFUSAL. `/credits/:id` and
   * `/transactions/:id` sit behind `Credits.Read` and `Transactions.Read`; offering the link to
   * somebody without the permission just moves the dead end one click further away. This is the
   * "hide, don't disable" rule applied to the affordance rather than to the value: the GUID itself
   * stays on screen either way, because it is still the thing they have to paste into a message to
   * whoever CAN open it. Only the ability to click it comes and goes.
   *
   * Payouts needs no check — this whole screen is behind `Payouts.Read`.
   */
  readonly canOpenCredits = computed(() => this.currentUser.hasPermission('Credits.Read'));
  readonly canOpenTransactions = computed(() => this.currentUser.hasPermission('Transactions.Read'));
  readonly bulkMarkPaidCount = signal(0);
  readonly bulkOverlapCount = signal(0);
  readonly bulkOverlapsLoading = signal(false);
  readonly activePeriod = signal<PeriodKey>('this-month');
  readonly hideZero = signal(true);

  readonly periodOptions: SegOption[] = [
    { value: 'this-month', label: 'PAYOUTS.FILTER.PERIOD_THIS_MONTH' },
    { value: 'last-month', label: 'PAYOUTS.FILTER.PERIOD_LAST_MONTH' },
    { value: 'ytd',        label: 'PAYOUTS.FILTER.PERIOD_YTD' },
    { value: 'all-time',   label: 'PAYOUTS.FILTER.PERIOD_ALL_TIME' },
  ];

  private readonly _payeeCache = new Map<string, string>();
  private readonly _planCache = new Map<string, string>();
  readonly selectedPayees = signal<{ id: string; label: string }[]>([]);
  readonly selectedPlans = signal<{ id: string; label: string }[]>([]);
  readonly selectedCurrencies = signal<string[]>([]);

  private static readonly _ALL_CURRENCIES: SelectOption[] = [
    'EUR', 'USD', 'GBP', 'PLN', 'CHF', 'JPY', 'CAD', 'AUD',
    'NOK', 'SEK', 'DKK', 'CZK', 'HUF', 'RON', 'BGN', 'MXN', 'BRL',
  ].map(c => ({ value: c, label: c }));

  readonly availableCurrencyOptions = computed<SelectOption[]>(() => {
    const selected = new Set(this.selectedCurrencies());
    return PayoutsListComponent._ALL_CURRENCIES.filter(o => !selected.has(o.value as string));
  });

  readonly statusOptions: SelectOption[] = [
    { value: 'All', label: 'PAYOUTS.FILTER.STATUS_ALL' },
    { value: 'Calculated', label: 'PAYOUTS.FILTER.STATUS_CALCULATED' },
    { value: 'Approved', label: 'PAYOUTS.FILTER.STATUS_APPROVED' },
    { value: 'Paid', label: 'PAYOUTS.FILTER.STATUS_PAID' },
    { value: 'Disputed', label: 'PAYOUTS.FILTER.STATUS_DISPUTED' },
  ];

  readonly form = new FormGroup({
    status: new FormControl<string>('All', { nonNullable: true }),
    periodFrom: new FormControl<string | null>(null),
    periodTo: new FormControl<string | null>(null),
    payeeSearch: new FormControl<string | number>('', { nonNullable: true }),
    planSearch: new FormControl<string | number>('', { nonNullable: true }),
    currencySearch: new FormControl<string | number>('', { nonNullable: true }),
  });

  readonly payeeSearchFn = (q: string) =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.fullName} (${p.employeeCode})`;
        this._payeeCache.set(p.id, label);
        return { value: p.id, label };
      })),
    );

  readonly planSearchFn = (q: string) =>
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q, filters: { statuses: 'Active,Archived' } }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.name} v${p.version}`;
        this._planCache.set(p.id, label);
        return {
          value: p.id,
          label,
          badge: {
            text: `PLANS.STATUS_${p.status.toUpperCase()}`,
            variant: (p.status === 'Active' ? 'success' : 'neutral') as BadgeVariant,
          },
        };
      })),
    );

  ngOnInit(): void {
    // SUBSCRIBE, don't snapshot. Angular reuses this component when only the query params change, so
    // ngOnInit runs once and a snapshot read there goes stale: arriving from the dashboard's
    // "Approved — Not Paid" card kept the previous filter until the user pressed reload, and leaving
    // via the sidebar's Payouts link left the Approved filter applied under a URL that no longer said
    // so. The URL is the filter, so the filter has to follow the URL every time it changes.
    //
    // No feedback loop: _syncUrl() writes with history.replaceState, which the router does not observe,
    // and the form is patched with emitEvent:false, so re-applying never re-triggers itself.
    this.route.queryParams
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(params => this._applyQueryParams(params as Record<string, string>));

    this._wireFormSubscriptions();
  }

  /** Puts the URL's filter into the store and the form. Runs on entry AND on every later change. */
  private _applyQueryParams(qp: Record<string, string>): void {
    const hasUrlParams = Object.keys(qp).length > 0;

    if (hasUrlParams) {
      this.store.loadFromQueryParams(qp);
      // Restore period key from URL if present; fall back to 'all-time' for custom date ranges.
      // A payment-date filter (from the dashboard's cash-flow card) is NOT a compensation-period
      // filter, so no period chip may light up for it: a lit chip invites a click, and clicking it
      // would add a period filter on top of the payment window and silently shrink the list below the
      // total the user came from. Checked first, so `period=` in the URL cannot override it.
      if (qp['payFrom'] || qp['payTo']) {
        this.activePeriod.set('all-time');
      } else if (qp['period'] && ['this-month', 'last-month', 'ytd', 'all-time'].includes(qp['period'])) {
        this.activePeriod.set(qp['period'] as PeriodKey);
      } else if (qp['pFrom'] || qp['pTo']) {
        this.activePeriod.set('all-time');
      }
      this.hideZero.set(qp['hz'] !== '0');
    } else {
      // No params means NO filter — the default view, not whatever was left over. clearFilters()
      // first, because _applyPeriod only sets the dates: without it, leaving the page through the
      // sidebar kept the previous status filter applied under a URL that no longer mentioned it.
      this.store.clearFilters();
      this.activePeriod.set('this-month');
      this.hideZero.set(true);
      this._applyPeriod('this-month');
    }

    this._syncFormFromStore();

    // Resolve plan names for any IDs restored from URL params that aren't yet cached.
    const uncachedPlanIds = this.store.filter().planIds.filter(id => !this._planCache.has(id));
    if (uncachedPlanIds.length > 0) {
      void this._resolvePlanNames(uncachedPlanIds);
    }

    // Refresh on route entry is handled centrally by [refreshOnEnter] (shared mechanism); the store's
    // constructor effect covers the first mount.
  }

  private _syncFormFromStore(): void {
    const f = this.store.filter();
    this.form.patchValue({
      status: f.status,
      periodFrom: f.periodFrom,
      periodTo: f.periodTo,
    }, { emitEvent: false });
    this.selectedPayees.set(f.payeeIds.map(id => ({ id, label: this._payeeCache.get(id) ?? id })));
    this.selectedPlans.set(f.planIds.map(id => ({ id, label: this._planCache.get(id) ?? id })));
    this.selectedCurrencies.set([...f.currencies]);
    if (f.payeeIds.length > 0 || f.currencies.length > 0 || f.periodFrom || f.periodTo) {
      this.filterOpen.set(true);
    }
  }

  private _wireFormSubscriptions(): void {
    const c = this.form.controls;

    c.status.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ status: v as PayoutStatus | 'All' }));

    c.periodFrom.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ periodFrom: v }));

    c.periodTo.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ periodTo: v }));

    c.payeeSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const idStr = String(v);
        if (this.store.filter().payeeIds.includes(idStr)) return;
        const label = this._payeeCache.get(idStr) ?? idStr;
        this.selectedPayees.update(ps => [...ps, { id: idStr, label }]);
        this._setFilter({ payeeIds: [...this.store.filter().payeeIds, idStr] });
        setTimeout(() => c.payeeSearch.setValue('', { emitEvent: false }), 0);
      });

    c.planSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const idStr = String(v);
        if (this.store.filter().planIds.includes(idStr)) return;
        const label = this._planCache.get(idStr) ?? idStr;
        this.selectedPlans.update(ps => [...ps, { id: idStr, label }]);
        this._setFilter({ planIds: [...this.store.filter().planIds, idStr] });
        setTimeout(() => c.planSearch.setValue('', { emitEvent: false }), 0);
      });

    c.currencySearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const code = String(v);
        if (this.store.filter().currencies.includes(code)) return;
        this.selectedCurrencies.update(cs => [...cs, code]);
        this._setFilter({ currencies: [...this.store.filter().currencies, code] });
        setTimeout(() => c.currencySearch.setValue('', { emitEvent: false }), 0);
      });
  }

  private _setFilter(partial: Partial<PayoutFilter>): void {
    this.store.setFilter(partial);
    this._syncUrl();
  }

  removePayee(id: string): void {
    this.selectedPayees.update(ps => ps.filter(p => p.id !== id));
    this._setFilter({ payeeIds: this.store.filter().payeeIds.filter(x => x !== id) });
  }

  removePlan(id: string): void {
    this.selectedPlans.update(ps => ps.filter(p => p.id !== id));
    this._setFilter({ planIds: this.store.filter().planIds.filter(x => x !== id) });
  }

  removeCurrency(code: string): void {
    this.selectedCurrencies.update(cs => cs.filter(c => c !== code));
    this._setFilter({ currencies: this.store.filter().currencies.filter(x => x !== code) });
  }

  setPeriod(key: string): void {
    this.activePeriod.set(key as PeriodKey);
    const { from, to } = this._computePeriodDates(key as PeriodKey);
    // Choosing a compensation period replaces any payment-date window carried in from the dashboard's
    // cash-flow card. Leaving both applied would intersect two different date questions and produce a
    // list nobody asked for.
    this._setFilter({ periodFrom: from, periodTo: to, paidFrom: null, paidTo: null });
    this.form.patchValue({ periodFrom: from, periodTo: to }, { emitEvent: false });
    this._syncUrl();
  }

  toggleHideZero(): void {
    const next = !this.hideZero();
    this.hideZero.set(next);
    this._setFilter({ hideZero: next });
  }

  clearFilters(): void {
    this.store.clearFilters();
    this.activePeriod.set('this-month');
    this.hideZero.set(true);
    const { from, to } = this._computePeriodDates('this-month');
    this.store.setFilter({ periodFrom: from, periodTo: to, hideZero: true });
    this.form.reset({ status: 'All', periodFrom: from, periodTo: to,
      payeeSearch: '', planSearch: '', currencySearch: '' });
    this.selectedPayees.set([]);
    this.selectedPlans.set([]);
    this.selectedCurrencies.set([]);
    this._syncUrl();
  }

  private _applyPeriod(key: PeriodKey): void {
    const { from, to } = this._computePeriodDates(key);
    this.store.setFilter({ periodFrom: from, periodTo: to });
  }

  private _computePeriodDates(key: PeriodKey): { from: string | null; to: string | null } {
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
        const f = `${first.getFullYear()}-${String(first.getMonth() + 1).padStart(2, '0')}-01`;
        const t = `${last.getFullYear()}-${String(last.getMonth() + 1).padStart(2, '0')}-${String(last.getDate()).padStart(2, '0')}`;
        return { from: f, to: t };
      }
      case 'ytd':
        return { from: `${yyyy}-01-01`, to: todayStr };
      case 'all-time':
        return { from: null, to: null };
    }
  }

  private async _resolvePlanNames(planIds: string[]): Promise<void> {
    const results = await Promise.all(
      planIds.map(id =>
        firstValueFrom(this.plansApi.getPlan(id)).then(p => ({ id, label: `${p.name} v${p.version}` })).catch(() => null)
      )
    );
    results.forEach(r => { if (r) this._planCache.set(r.id, r.label); });
    this.selectedPlans.update(ps =>
      ps.map(p => ({ ...p, label: this._planCache.get(p.id) ?? p.label }))
    );
  }

  readonly exporting = signal(false);
  readonly exportError = signal<string | null>(null);
  private static readonly EXPORT_CONFIRM_THRESHOLD = 50_000;

  async onExport(): Promise<void> {
    if (this.exporting()) return;

    const total = this.store.totalCount();
    if (total > PayoutsListComponent.EXPORT_CONFIRM_THRESHOLD) {
      const msg = `This export contains ${total.toLocaleString()} rows and may take a moment. Continue?`;
      if (!window.confirm(msg)) return;
    }

    this.exporting.set(true);
    this.exportError.set(null);
    try {
      const blob = await firstValueFrom(this.api.exportToExcel(this.store.toExportParams()));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `payouts-export-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      this.exportError.set('PAYOUTS.EXPORT.ERROR');
    } finally {
      this.exporting.set(false);
    }
  }

  readonly bulkMarkPaidTotals = computed(() => {
    const { totalsByCurrency } = this.store.bulkMarkPaidSummary();
    return [...totalsByCurrency.entries()].map(([currency, amount]) => ({ currency, amount }));
  });

  async openBulkApproveConfirm(): Promise<void> {
    this.bulkOverlapCount.set(0);
    this.bulkOverlapsLoading.set(true);
    this.bulkApproveConfirmOpen.set(true);
    try {
      const ids = this.store.selectedCalculatedIds();
      const res = await firstValueFrom(this.api.checkBulkOverlaps(ids));
      this.bulkOverlapCount.set(res.count);
    } catch {
      // non-critical
    } finally {
      this.bulkOverlapsLoading.set(false);
    }
  }

  async openBulkMarkPaidConfirm(): Promise<void> {
    this.bulkOverlapCount.set(0);
    this.bulkOverlapsLoading.set(true);
    this.bulkMarkPaidConfirmOpen.set(true);
    try {
      const ids = this.store.selectedApprovedIds();
      const res = await firstValueFrom(this.api.checkBulkOverlaps(ids));
      this.bulkOverlapCount.set(res.count);
    } catch {
      // non-critical
    } finally {
      this.bulkOverlapsLoading.set(false);
    }
  }

  async onBulkMarkPaid(): Promise<void> {
    const ids = this.store.selectedApprovedIds();
    if (ids.length === 0 || this.bulkMarkPaiding()) return;
    this.bulkMarkPaidConfirmOpen.set(false);
    this.bulkMarkPaiding.set(true);
    this.bulkMarkPaidErrors.set([]);
    this.bulkMarkPaidCount.set(0);
    try {
      const result = await firstValueFrom(this.api.bulkMarkPaid({ payoutIds: ids }));
      if (result.errors.length > 0) {
        this.bulkMarkPaidErrors.set(result.errors);
        this.bulkMarkPaidCount.set(result.paid);
      }
      if (result.paid > 0) {
        this.store.clearSelection();
        await this.store.reload();
      }
    } catch {
      this.bulkMarkPaidErrors.set(['PAYOUTS.BULK_MARK_PAID_ERROR']);
    } finally {
      this.bulkMarkPaiding.set(false);
    }
  }

  async onBulkApprove(): Promise<void> {
    const ids = this.store.selectedCalculatedIds();
    if (ids.length === 0 || this.bulkApproving()) return;
    this.bulkApproveConfirmOpen.set(false);
    this.bulkApproving.set(true);
    try {
      const result = await firstValueFrom(this.api.bulkApprove({ payoutIds: ids }));
      this.store.clearSelection();
      await this.store.reload();
      // Report result — keep simple without toast dependency for now
      console.info(`Bulk approve: ${result.approved} approved, ${result.errors.length} errors`);
    } finally {
      this.bulkApproving.set(false);
    }
  }

  statusBadge(status: PayoutStatus): BadgeVariant {
    switch (status) {
      case 'Calculated': return 'neutral';
      case 'Approved': return 'brand';
      case 'Paid': return 'success';
      case 'Disputed': return 'danger';
      // ★ NEUTRAL, NOT DANGER. A discarded payout is closed paperwork, not a problem: the money it
      // described was already paid by another payout. Colouring it red would put an alarm on a queue
      // that is finally clean.
      case 'Discarded': return 'neutral';
    }
  }

  statusLabel(status: PayoutStatus): string {
    return `PAYOUTS.STATUS_${status.toUpperCase()}`;
  }

  viewPayout(id: string): void {
    window.open(`/payouts/${id}`, '_blank');
  }

  formatPeriod(start: string, end: string): string {
    return `${start} → ${end}`;
  }

  private _syncUrl(): void {
    const qp = this.store.toQueryParams();
    const period = this.activePeriod();
    if (period !== 'this-month') qp['period'] = period;
    const suffix = Object.keys(qp).length > 0
      ? '?' + new URLSearchParams(qp).toString() : '';
    window.history.replaceState(null, '', window.location.pathname + suffix);
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
