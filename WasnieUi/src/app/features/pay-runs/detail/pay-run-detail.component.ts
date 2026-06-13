import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { distinctUntilChanged } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { map } from 'rxjs/operators';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { PayRunDetailStore } from '../state/pay-run-detail.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunStatus } from '../models/pay-run.model';
import { PayoutStatus } from '../../payouts/models/payout.model';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import {
  WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
  WsTableComponent, WsTableEmptyComponent, WsPaginationComponent, WsModalComponent,
  WsSelectComponent, WsInputComponent, WsDatePickerComponent,
  type BadgeVariant, type SelectOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-pay-run-detail',
  standalone: true,
  providers: [PayRunDetailStore],
  imports: [
    AppShellComponent, RouterLink, ReactiveFormsModule, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
    WsTableComponent, WsTableEmptyComponent, WsPaginationComponent, WsModalComponent,
    WsSelectComponent, WsInputComponent, WsDatePickerComponent,
  ],
  templateUrl: './pay-run-detail.component.html',
  styleUrl: './pay-run-detail.component.scss',
})
export class PayRunDetailComponent implements OnInit {
  readonly store = inject(PayRunDetailStore);
  private readonly api = inject(PayRunsApiService);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly plansApi = inject(PlansApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly runId = this.route.snapshot.paramMap.get('id')!;

  readonly approveConfirmOpen = signal(false);
  readonly markPaidConfirmOpen = signal(false);
  readonly reopenConfirmOpen = signal(false);
  readonly actioning = signal(false);
  readonly actionError = signal<string | null>(null);

  // Filter panel
  readonly filterOpen = signal(false);
  readonly exporting = signal(false);
  readonly exportError = signal<string | null>(null);

  // Chip state for payee/plan multi-select
  private readonly _payeeCache = new Map<string, string>();
  private readonly _planCache = new Map<string, string>();
  readonly selectedPayees = signal<{ id: string; label: string }[]>([]);
  readonly selectedPlans = signal<{ id: string; label: string }[]>([]);

  readonly payoutStatusOptions: SelectOption[] = [
    { value: '', label: 'PAY_RUNS.DETAIL.FILTER.STATUS_ALL' },
    { value: 'Calculated', label: 'PAY_RUNS.DETAIL.FILTER.STATUS_CALCULATED' },
    { value: 'Approved', label: 'PAY_RUNS.DETAIL.FILTER.STATUS_APPROVED' },
    { value: 'Paid', label: 'PAY_RUNS.DETAIL.FILTER.STATUS_PAID' },
    { value: 'Disputed', label: 'PAY_RUNS.DETAIL.FILTER.STATUS_DISPUTED' },
  ];

  readonly filterForm = new FormGroup({
    status: new FormControl<string>('', { nonNullable: true }),
    periodFrom: new FormControl<string | null>(null),
    periodTo: new FormControl<string | null>(null),
    amountMin: new FormControl<string>('', { nonNullable: true }),
    amountMax: new FormControl<string>('', { nonNullable: true }),
    payeeSearch: new FormControl<string | number>('', { nonNullable: true }),
    planSearch: new FormControl<string | number>('', { nonNullable: true }),
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
        const label = `${p.name}`;
        this._planCache.set(p.id, label);
        return { value: p.id, label };
      })),
    );

  ngOnInit(): void {
    void this.store.load(this.runId);
    this._wireFilterSubscriptions();
  }

  private _wireFilterSubscriptions(): void {
    const c = this.filterForm.controls;

    c.status.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this.store.setFilter({ status: v || null }));

    c.periodFrom.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this.store.setFilter({ periodFrom: v }));

    c.periodTo.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this.store.setFilter({ periodTo: v }));

    c.amountMin.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        const n = parseFloat(v);
        this.store.setFilter({ amountMin: isNaN(n) ? null : n });
      });

    c.amountMax.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        const n = parseFloat(v);
        this.store.setFilter({ amountMax: isNaN(n) ? null : n });
      });

    c.payeeSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const id = String(v);
        if (this.store.filter().payeeIds.includes(id)) return;
        this.selectedPayees.update(arr => [...arr, { id, label: this._payeeCache.get(id) ?? id }]);
        this.store.setFilter({ payeeIds: [...this.store.filter().payeeIds, id] });
        c.payeeSearch.setValue('', { emitEvent: false });
      });

    c.planSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const id = String(v);
        if (this.store.filter().planIds.includes(id)) return;
        this.selectedPlans.update(arr => [...arr, { id, label: this._planCache.get(id) ?? id }]);
        this.store.setFilter({ planIds: [...this.store.filter().planIds, id] });
        c.planSearch.setValue('', { emitEvent: false });
      });
  }

  removePayee(id: string): void {
    this.selectedPayees.update(arr => arr.filter(p => p.id !== id));
    this.store.setFilter({ payeeIds: this.store.filter().payeeIds.filter(x => x !== id) });
  }

  removePlan(id: string): void {
    this.selectedPlans.update(arr => arr.filter(p => p.id !== id));
    this.store.setFilter({ planIds: this.store.filter().planIds.filter(x => x !== id) });
  }

  clearFilters(): void {
    this.filterForm.reset({ status: '', periodFrom: null, periodTo: null, amountMin: '', amountMax: '', payeeSearch: '', planSearch: '' }, { emitEvent: false });
    this.selectedPayees.set([]);
    this.selectedPlans.set([]);
    this.store.clearFilters();
  }

  async onExportPayouts(): Promise<void> {
    if (this.exporting()) return;
    this.exporting.set(true);
    this.exportError.set(null);
    try {
      const blob = await firstValueFrom(this.api.exportRunPayouts(this.runId, this.store.toExportParams()));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `payouts-run-${this.runId.slice(0, 8)}-${new Date().toISOString().slice(0, 10)}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      this.exportError.set('PAY_RUNS.DETAIL.EXPORT.ERROR');
    } finally {
      this.exporting.set(false);
    }
  }

  runStatusBadge(status: PayRunStatus): BadgeVariant {
    switch (status) {
      case 'Draft':    return 'neutral';
      case 'Approved': return 'brand';
      case 'Paid':     return 'success';
    }
  }

  payoutStatusBadge(status: PayoutStatus): BadgeVariant {
    switch (status) {
      case 'Calculated': return 'neutral';
      case 'Approved':   return 'brand';
      case 'Paid':       return 'success';
      case 'Disputed':   return 'danger';
    }
  }

  async onApprove(): Promise<void> {
    if (this.actioning()) return;
    this.approveConfirmOpen.set(false);
    this.actioning.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.approve(this.runId));
      await this.store.reload();
    } catch (err) {
      const apiMsg = (err as { error?: { message?: string } })?.error?.message;
      this.actionError.set(apiMsg ?? 'PAY_RUNS.DETAIL.APPROVE_ERROR');
    } finally {
      this.actioning.set(false);
    }
  }

  async onMarkPaid(): Promise<void> {
    if (this.actioning()) return;
    this.markPaidConfirmOpen.set(false);
    this.actioning.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.markPaid(this.runId));
      await this.store.reload();
    } catch (err) {
      const apiMsg = (err as { error?: { message?: string } })?.error?.message;
      this.actionError.set(apiMsg ?? 'PAY_RUNS.DETAIL.MARK_PAID_ERROR');
    } finally {
      this.actioning.set(false);
    }
  }

  async onReopen(): Promise<void> {
    if (this.actioning()) return;
    this.reopenConfirmOpen.set(false);
    this.actioning.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.reopen(this.runId));
      await this.store.reload();
    } catch (err) {
      const apiMsg = (err as { error?: { message?: string } })?.error?.message;
      this.actionError.set(apiMsg ?? 'PAY_RUNS.DETAIL.REOPEN_ERROR');
    } finally {
      this.actioning.set(false);
    }
  }

  viewPayout(id: string): void {
    window.open(`/payouts/${id}`, '_blank');
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 6 }, (_, i) => i);
  }
}
