import {
  Component, DestroyRef, effect, inject, OnInit, signal, untracked, viewChild,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { distinctUntilChanged } from 'rxjs/operators';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunsStore, PayRunFilter } from '../state/pay-runs.store';
import { PayRunListItem, PayRunStatus, CalculatePayRunResult } from '../models/pay-run.model';
import { WsDatePickerComponent as DatePickerRef } from '../../../shared/ui/ws-date-picker/ws-date-picker.component';
import {
  WsButtonComponent, WsBadgeComponent, WsCardComponent, WsSelectComponent,
  WsDatePickerComponent, WsPageLayoutComponent, WsTableComponent, WsTableEmptyComponent,
  WsPaginationComponent, WsModalComponent, WsSegmentedControlComponent,
  type BadgeVariant, type SelectOption, type SegOption,
} from '../../../shared/ui';

type PeriodKey = 'this-month' | 'last-month' | 'ytd' | 'all-time';

@Component({
  selector: 'app-pay-runs-list',
  standalone: true,
  imports: [
    AppShellComponent, ReactiveFormsModule, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    WsButtonComponent, WsBadgeComponent, WsCardComponent,
    WsSelectComponent, WsDatePickerComponent, WsSegmentedControlComponent,
    WsPageLayoutComponent, WsTableComponent, WsTableEmptyComponent,
    WsPaginationComponent, WsModalComponent,
  ],
  templateUrl: './pay-runs-list.component.html',
  styleUrl: './pay-runs-list.component.scss',
})
export class PayRunsListComponent implements OnInit {
  readonly store = inject(PayRunsStore);
  private readonly api = inject(PayRunsApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  // Filter bar date pickers
  private readonly _filterFromPicker = viewChild<DatePickerRef>('filterFromPicker');
  private readonly _filterToPicker = viewChild<DatePickerRef>('filterToPicker');
  // Calculate modal date pickers
  private readonly _startPicker = viewChild<DatePickerRef>('startPicker');
  private readonly _endPicker = viewChild<DatePickerRef>('endPicker');

  readonly activePeriod = signal<PeriodKey>('this-month');
  readonly calculateModalOpen = signal(false);
  readonly calculating = signal(false);
  readonly calculatePhase = signal<'form' | 'done'>('form');
  readonly calculateResult = signal<CalculatePayRunResult | null>(null);
  readonly calculateError = signal<string | null>(null);

  readonly exporting = signal(false);
  readonly exportError = signal<string | null>(null);

  readonly deleteTarget = signal<PayRunListItem | null>(null);
  readonly deleting = signal(false);
  readonly deleteError = signal<string | null>(null);

  readonly periodOptions: SegOption[] = [
    { value: 'this-month', label: 'PAY_RUNS.FILTER.PERIOD_THIS_MONTH' },
    { value: 'last-month', label: 'PAY_RUNS.FILTER.PERIOD_LAST_MONTH' },
    { value: 'ytd',        label: 'PAY_RUNS.FILTER.PERIOD_YTD' },
    { value: 'all-time',   label: 'PAY_RUNS.FILTER.PERIOD_ALL_TIME' },
  ];

  readonly statusOptions: SelectOption[] = [
    { value: 'All',      label: 'PAY_RUNS.FILTER.STATUS_ALL' },
    { value: 'Draft',    label: 'PAY_RUNS.STATUS_DRAFT' },
    { value: 'Approved', label: 'PAY_RUNS.STATUS_APPROVED' },
    { value: 'Paid',     label: 'PAY_RUNS.STATUS_PAID' },
  ];

  readonly form = new FormGroup({
    status: new FormControl<string>('All', { nonNullable: true }),
    periodFrom: new FormControl<string | null>(null),
    periodTo: new FormControl<string | null>(null),
  });

  readonly calculateForm = new FormGroup({
    periodStart: new FormControl<string | null>(null),
    periodEnd: new FormControl<string | null>(null),
  });

  constructor() {
    // Filter bar: close to/from when the other opens
    effect(() => {
      const from = this._filterFromPicker();
      const to = this._filterToPicker();
      if (from?.isOpen()) untracked(() => to?.close());
    });
    effect(() => {
      const from = this._filterFromPicker();
      const to = this._filterToPicker();
      if (to?.isOpen()) untracked(() => from?.close());
    });
    // Calculate modal
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

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParams as Record<string, string>;

    // Resolve initial status + period from URL params (e.g. ?status=Draft from dashboard)
    const validStatuses: PayRunStatus[] = ['Draft', 'Approved', 'Paid'];
    const urlStatus = qp['status'] as PayRunStatus | undefined;
    const status: PayRunStatus | 'All' =
      urlStatus && (validStatuses as string[]).includes(urlStatus) ? urlStatus : 'All';

    const validPeriods: PeriodKey[] = ['this-month', 'last-month', 'ytd', 'all-time'];
    const urlPeriod = qp['period'] as PeriodKey | undefined;
    const periodKey: PeriodKey =
      urlPeriod && (validPeriods as string[]).includes(urlPeriod) ? urlPeriod : 'this-month';

    this.activePeriod.set(periodKey);
    const { from, to } = this._computePeriodDates(periodKey);
    this.form.patchValue({ status, periodFrom: from, periodTo: to }, { emitEvent: false });
    this.store.setFilter({ status, periodFrom: from, periodTo: to });

    this._wireFormSubscriptions();
  }

  private _wireFormSubscriptions(): void {
    const c = this.form.controls;

    c.status.valueChanges
      .pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this.store.setFilter({ status: v as PayRunStatus | 'All' }));

    c.periodFrom.valueChanges
      .pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        // Switch segment to 'all-time' so the quick selector doesn't show a stale highlight
        if (v) this.activePeriod.set('all-time');
        this.store.setFilter({ periodFrom: v });
      });

    c.periodTo.valueChanges
      .pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (v) this.activePeriod.set('all-time');
        this.store.setFilter({ periodTo: v });
      });
  }

  setPeriod(key: string): void {
    this.activePeriod.set(key as PeriodKey);
    const { from, to } = this._computePeriodDates(key as PeriodKey);
    // Patch form without emitting to avoid double store.setFilter
    this.form.patchValue({ periodFrom: from, periodTo: to }, { emitEvent: false });
    this.store.setFilter({ periodFrom: from, periodTo: to });
  }

  private _applyPeriod(key: PeriodKey): void {
    const { from, to } = this._computePeriodDates(key);
    this.form.patchValue({ periodFrom: from, periodTo: to }, { emitEvent: false });
    this.store.setFilter({ periodFrom: from, periodTo: to });
  }

  private _computePeriodDates(key: PeriodKey): { from: string | null; to: string | null } {
    const today = new Date();
    const yyyy = today.getFullYear();
    const mm = String(today.getMonth() + 1).padStart(2, '0');
    const dd = String(today.getDate()).padStart(2, '0');
    const todayStr = `${yyyy}-${mm}-${dd}`;

    switch (key) {
      case 'this-month': {
        const lastDay = new Date(yyyy, today.getMonth() + 1, 0);
        const lastDayStr = `${yyyy}-${mm}-${String(lastDay.getDate()).padStart(2, '0')}`;
        return { from: `${yyyy}-${mm}-01`, to: lastDayStr };
      }
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

  async onCalculate(): Promise<void> {
    const { periodStart, periodEnd } = this.calculateForm.value;
    if (!periodStart || !periodEnd || this.calculating()) return;
    this.calculating.set(true);
    this.calculateError.set(null);
    this.calculateResult.set(null);
    try {
      const result = await firstValueFrom(this.api.calculate({ periodStart, periodEnd }));
      this.calculateResult.set(result);
      this.calculatePhase.set('done');
      await this.store.reload();
    } catch (err) {
      const apiMsg = (err as { error?: { message?: string } })?.error?.message;
      this.calculateError.set(apiMsg ?? 'PAY_RUNS.CALCULATE_ERROR');
    } finally {
      this.calculating.set(false);
    }
  }

  closeCalculateModal(): void {
    this.calculateModalOpen.set(false);
    this.calculatePhase.set('form');
    this.calculateResult.set(null);
    this.calculateError.set(null);
    this.calculateForm.reset();
  }

  async onExport(): Promise<void> {
    if (this.exporting()) return;
    this.exporting.set(true);
    this.exportError.set(null);
    try {
      const blob = await firstValueFrom(this.api.exportPayRuns(this.store.toExportParams()));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `pay-runs-export-${new Date().toISOString().slice(0, 10)}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      this.exportError.set('PAY_RUNS.EXPORT.ERROR');
    } finally {
      this.exporting.set(false);
    }
  }

  openDeleteConfirm(run: PayRunListItem, event: MouseEvent): void {
    event.stopPropagation();
    this.deleteTarget.set(run);
    this.deleteError.set(null);
  }

  async onDeleteConfirmed(): Promise<void> {
    const target = this.deleteTarget();
    if (!target || this.deleting()) return;
    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      await firstValueFrom(this.api.deleteDraft(target.id));
      this.deleteTarget.set(null);
      await this.store.reload();
    } catch (err) {
      const apiMsg = (err as { error?: { message?: string } })?.error?.message;
      this.deleteError.set(apiMsg ?? 'PAY_RUNS.DELETE_ERROR');
    } finally {
      this.deleting.set(false);
    }
  }

  navigateToRun(id: string): void {
    void this.router.navigate(['/pay-runs', id]);
  }

  statusBadge(status: PayRunStatus): BadgeVariant {
    switch (status) {
      case 'Draft':    return 'neutral';
      case 'Approved': return 'brand';
      case 'Paid':     return 'success';
    }
  }

  totalAmountsEntries(run: PayRunListItem): { currency: string; amount: number }[] {
    return Object.entries(run.totalAmounts).map(([currency, amount]) => ({ currency, amount }));
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 6 }, (_, i) => i);
  }
}
