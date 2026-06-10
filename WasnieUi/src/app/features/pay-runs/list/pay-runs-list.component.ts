import {
  Component, DestroyRef, effect, inject, OnInit, signal, untracked, viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
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
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  private readonly _startPicker = viewChild<DatePickerRef>('startPicker');
  private readonly _endPicker = viewChild<DatePickerRef>('endPicker');

  readonly activePeriod = signal<PeriodKey>('last-month');
  readonly calculateModalOpen = signal(false);
  readonly calculating = signal(false);
  readonly calculatePhase = signal<'form' | 'done'>('form');
  readonly calculateResult = signal<CalculatePayRunResult | null>(null);
  readonly calculateError = signal<string | null>(null);

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
  });

  readonly calculateForm = new FormGroup({
    periodStart: new FormControl<string | null>(null),
    periodEnd: new FormControl<string | null>(null),
  });

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

  ngOnInit(): void {
    this._applyPeriod('last-month');
    this._wireFormSubscriptions();
  }

  private _wireFormSubscriptions(): void {
    this.form.controls.status.valueChanges
      .pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this.store.setFilter({ status: v as PayRunStatus | 'All' }));
  }

  setPeriod(key: string): void {
    this.activePeriod.set(key as PeriodKey);
    const { from, to } = this._computePeriodDates(key as PeriodKey);
    this.store.setFilter({ periodFrom: from, periodTo: to });
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
    } catch {
      this.calculateError.set('PAY_RUNS.CALCULATE_ERROR');
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
