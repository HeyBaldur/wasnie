import {
  Component, computed, effect, inject, OnInit, signal, DestroyRef, untracked, viewChild,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { distinctUntilChanged, map, switchMap } from 'rxjs/operators';
import { firstValueFrom, interval } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PayoutsStore, PayoutFilter, EMPTY_PAYOUT_FILTER } from '../state/payouts.store';
import { PayoutStatus } from '../models/payout.model';
import { WsDatePickerComponent as DatePickerRef } from '../../../shared/ui/ws-date-picker/ws-date-picker.component';
import { PayoutJobStatus } from '../services/payouts.api.service';
import { CalculateJobResult } from '../models/payout.model';
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
  WsConfirmationModalComponent,
  type BadgeVariant,
  type SelectOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-payouts-list',
  standalone: true,
  imports: [
    AppShellComponent, RouterLink, ReactiveFormsModule, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    WsButtonComponent, WsBadgeComponent, WsCardComponent,
    WsSelectComponent, WsDatePickerComponent,
    WsPageLayoutComponent, WsTableComponent, WsTableEmptyComponent,
    WsPaginationComponent, WsModalComponent, WsConfirmationModalComponent,
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

  // Period picker mutual exclusion — closing one when the other opens
  private readonly _startPicker = viewChild<DatePickerRef>('startPicker');
  private readonly _endPicker = viewChild<DatePickerRef>('endPicker');

  readonly filterOpen = signal(false);
  readonly calculateModalOpen = signal(false);
  readonly bulkApproveConfirmOpen = signal(false);
  readonly bulkApproving = signal(false);
  readonly calculating = signal(false);
  readonly calculatePhase = signal<'form' | 'running' | 'done'>('form');
  readonly calculateResult = signal<CalculateJobResult | null>(null);
  readonly calculateError = signal<string | null>(null);

  constructor() {
    effect(() => {
      const start = this._startPicker();
      const end = this._endPicker();
      if (start?.isOpen()) untracked(() => end?.close());
    });
    effect(() => {
      const start = this._startPicker();
      const end = this._endPicker();
      if (end?.isOpen()) untracked(() => start?.close());
    });
  }

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

  readonly calculateForm = new FormGroup({
    periodStart: new FormControl<string | null>(null),
    periodEnd: new FormControl<string | null>(null),
    payeeFilter: new FormControl<string | number>('', { nonNullable: true }),
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
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.name} v${p.version}`;
        this._planCache.set(p.id, label);
        return { value: p.id, label };
      })),
    );

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParams as Record<string, string>;
    if (Object.keys(qp).length > 0) {
      this.store.loadFromQueryParams(qp);
    }
    this._syncFormFromStore();
    this._wireFormSubscriptions();
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

  clearFilters(): void {
    this.store.clearFilters();
    this.form.reset({ status: 'All', periodFrom: null, periodTo: null,
      payeeSearch: '', planSearch: '', currencySearch: '' });
    this.selectedPayees.set([]);
    this.selectedPlans.set([]);
    this.selectedCurrencies.set([]);
    this._syncUrl();
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

  async onCalculate(): Promise<void> {
    const { periodStart, periodEnd, payeeFilter } = this.calculateForm.value;
    if (!periodStart || !periodEnd || this.calculating()) return;
    this.calculating.set(true);
    this.calculateError.set(null);
    this.calculateResult.set(null);
    try {
      const { jobId } = await firstValueFrom(this.api.calculate({
        periodStart,
        periodEnd,
        payeeIdFilter: payeeFilter ? String(payeeFilter) : null,
      }));
      this.calculatePhase.set('running');
      this._pollJob(jobId);
    } catch {
      this.calculateError.set('PAYOUTS.CALCULATE_ERROR');
      this.calculating.set(false);
    }
  }

  private _pollJob(jobId: string): void {
    interval(2000).pipe(
      switchMap(() => this.api.getJobStatus(jobId)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (status: PayoutJobStatus) => {
        if (status.state === 'Succeeded') {
          this._onJobDone(status);
        } else if (status.state === 'Failed' || status.state === 'Cancelled') {
          this.calculateError.set(status.errorMessage ?? 'PAYOUTS.CALCULATE_ERROR');
          this.calculatePhase.set('form');
          this.calculating.set(false);
        }
        // Pending / Running — keep polling
      },
      error: () => {
        this.calculateError.set('PAYOUTS.CALCULATE_ERROR');
        this.calculatePhase.set('form');
        this.calculating.set(false);
      },
    });
  }

  private _onJobDone(status: PayoutJobStatus): void {
    let result: CalculateJobResult = { payoutsCreated: 0, conflicts: [], warnings: [] };
    if (status.resultSummary) {
      try { result = JSON.parse(status.resultSummary) as CalculateJobResult; } catch { /* keep default */ }
    }
    this.calculateResult.set(result);
    this.calculatePhase.set('done');
    this.calculating.set(false);
    this.store.reload();
  }

  closeCalculateModal(): void {
    const wasDone = this.calculatePhase() === 'done';
    this.calculateModalOpen.set(false);
    this.calculatePhase.set('form');
    this.calculateResult.set(null);
    this.calculateError.set(null);
    if (wasDone) this.calculateForm.reset();
  }

  statusBadge(status: PayoutStatus): BadgeVariant {
    switch (status) {
      case 'Calculated': return 'neutral';
      case 'Approved': return 'brand';
      case 'Paid': return 'success';
      case 'Disputed': return 'danger';
    }
  }

  statusLabel(status: PayoutStatus): string {
    return `PAYOUTS.STATUS_${status.toUpperCase()}`;
  }

  formatPeriod(start: string, end: string): string {
    return `${start} → ${end}`;
  }

  private _syncUrl(): void {
    const qp = this.store.toQueryParams();
    const suffix = Object.keys(qp).length > 0
      ? '?' + new URLSearchParams(qp).toString() : '';
    window.history.replaceState(null, '', window.location.pathname + suffix);
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
