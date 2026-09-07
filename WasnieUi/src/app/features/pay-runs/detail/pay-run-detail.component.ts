import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { distinctUntilChanged } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { map } from 'rxjs/operators';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { OverlapWarningComponent } from '../../../shared/components/overlap-warning/overlap-warning.component';
import { OverlapRow } from '../../../shared/models/overlap-row.model';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { PayRunDetailStore } from '../state/pay-run-detail.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { OverlappingPayRun, PayRunStatus } from '../models/pay-run.model';
import { PaymentConflictItem, PayoutStatus } from '../../payouts/models/payout.model';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { CreditsApiService } from '../../credits/services/credits.api.service';
import { BlockingPayRunInfo } from '../../credits/models/credit.model';
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
    OverlapWarningComponent,
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
  private readonly creditsApi = inject(CreditsApiService);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly plansApi = inject(PlansApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly runId = this.route.snapshot.paramMap.get('id')!;

  readonly approveConfirmOpen = signal(false);
  readonly markPaidConfirmOpen = signal(false);
  readonly reopenConfirmOpen = signal(false);
  readonly deleteConfirmOpen = signal(false);
  readonly recalculateConfirmOpen = signal(false);
  readonly actioning = signal(false);
  readonly actionError = signal<string | null>(null);
  readonly recalculating = signal(false);
  readonly recalculateError = signal<string | null>(null);
  readonly recalculateResult = signal<{ supersededCount: number; jobCount: number; deletedDraftCount: number } | null>(null);
  readonly recalculateBlockedRuns = signal<OverlapRow[]>([]);
  readonly doublePayConflicts = signal<OverlapRow[]>([]);
  readonly deleting = signal(false);
  readonly deleteError = signal<string | null>(null);

  readonly approveOverlaps = signal<OverlappingPayRun[]>([]);
  readonly markPaidOverlaps = signal<OverlappingPayRun[]>([]);
  readonly overlapsLoading = signal(false);

  readonly approveOverlapRows = computed<OverlapRow[]>(() =>
    this.approveOverlaps().map(r => this._runToRow(r))
  );

  readonly markPaidOverlapRows = computed<OverlapRow[]>(() =>
    this.markPaidOverlaps().map(r => this._runToRow(r))
  );

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
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q, filters: { statuses: 'Active,Archived' } }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.name}`;
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

  async openApproveConfirm(): Promise<void> {
    this.approveOverlaps.set([]);
    this.approveConfirmOpen.set(true);
    this.overlapsLoading.set(true);
    try {
      const overlaps = await firstValueFrom(this.api.getOverlaps(this.runId));
      this.approveOverlaps.set(overlaps);
    } catch {
      // non-critical — modal is already open, overlap info unavailable
    } finally {
      this.overlapsLoading.set(false);
    }
  }

  async openMarkPaidConfirm(): Promise<void> {
    this.markPaidOverlaps.set([]);
    this.markPaidConfirmOpen.set(true);
    this.overlapsLoading.set(true);
    try {
      const overlaps = await firstValueFrom(this.api.getOverlaps(this.runId));
      this.markPaidOverlaps.set(overlaps);
    } catch {
      // non-critical — modal is already open, overlap info unavailable
    } finally {
      this.overlapsLoading.set(false);
    }
  }

  private _runToRow(run: OverlappingPayRun): OverlapRow {
    return {
      id: run.id,
      periodStart: run.periodStart,
      periodEnd: run.periodEnd,
      statusLabel: `PAY_RUNS.STATUS_${run.status.toUpperCase()}`,
      statusVariant: run.status === 'Paid' ? 'success' : 'brand',
      col3: String(run.payeeCount),
      amounts: Object.entries(run.totalAmounts).map(([currency, amount]) => ({ currency, amount })),
    };
  }

  viewOverlapRun(id: string): void {
    window.open(`/pay-runs/${id}`, '_blank');
  }

  viewConflictPayout(payoutId: string): void {
    window.open(`/payouts/${payoutId}`, '_blank');
  }

  private _toConflictRows(conflicts: PaymentConflictItem[]): OverlapRow[] {
    return conflicts.map(c => ({
      id: c.paidInPayoutId,
      periodStart: c.paidInPayoutPeriodStart,
      periodEnd: c.paidInPayoutPeriodEnd,
      statusLabel: 'PAYOUTS.STATUS_PAID',
      statusVariant: 'success' as BadgeVariant,
      col3: c.transactionReference,
      amounts: [],
    }));
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
      // ★ Closed paperwork, not a problem — see the same case in the payouts list.
      case 'Discarded':  return 'neutral';
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
    this.doublePayConflicts.set([]);
    try {
      await firstValueFrom(this.api.markPaid(this.runId));
      await this.store.reload();
    } catch (err) {
      const httpErr = err as { status?: number; error?: { blocked?: boolean; conflicts?: PaymentConflictItem[]; message?: string } };
      if (httpErr.status === 409 && httpErr.error?.blocked && httpErr.error.conflicts?.length) {
        this.doublePayConflicts.set(this._toConflictRows(httpErr.error.conflicts));
      } else {
        this.actionError.set(httpErr.error?.message ?? 'PAY_RUNS.DETAIL.MARK_PAID_ERROR');
      }
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

  async onRecalculate(): Promise<void> {
    if (this.recalculating()) return;
    const run = this.store.run();
    if (!run) return;
    this.recalculateConfirmOpen.set(false);
    this.recalculating.set(true);
    this.recalculateError.set(null);
    this.recalculateResult.set(null);
    this.recalculateBlockedRuns.set([]);
    try {
      const result = await firstValueFrom(this.creditsApi.recalculate(run.periodStart, run.periodEnd));
      this.recalculateResult.set({
        supersededCount: result.supersededCount,
        jobCount: result.jobIds.length,
        deletedDraftCount: result.deletedDraftCount,
      });
      if (result.deletedDraftCount > 0) {
        // This draft pay run was auto-deleted — navigating keeps the user from hitting a 404 reload.
        await this.router.navigate(['/pay-runs']);
      } else {
        await this.store.reload();
      }
    } catch (err) {
      const httpErr = err as {
        status?: number;
        error?: { blocked?: boolean; blockingPayRuns?: BlockingPayRunInfo[]; message?: string };
      };
      if (httpErr.status === 409 && httpErr.error?.blocked && httpErr.error.blockingPayRuns?.length) {
        this.recalculateBlockedRuns.set(this._blockingRunsToRows(httpErr.error.blockingPayRuns));
      } else {
        this.recalculateError.set(httpErr.error?.message ?? 'PAY_RUNS.DETAIL.RECALCULATE_ERROR');
      }
    } finally {
      this.recalculating.set(false);
    }
  }

  private _blockingRunsToRows(runs: BlockingPayRunInfo[]): OverlapRow[] {
    return runs.map(r => ({
      id: r.payRunId,
      periodStart: r.periodStart,
      periodEnd: r.periodEnd,
      statusLabel: `PAY_RUNS.STATUS_${r.status.toUpperCase()}`,
      statusVariant: r.status === 'Paid' ? 'success' : ('brand' as const),
      col3: '',
      amounts: [],
    }));
  }

  async onDelete(): Promise<void> {
    if (this.deleting()) return;
    this.deleteConfirmOpen.set(false);
    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      await firstValueFrom(this.api.deleteDraft(this.runId));
      await this.router.navigate(['/pay-runs']);
    } catch (err) {
      const apiMsg = (err as { error?: { message?: string } })?.error?.message;
      this.deleteError.set(apiMsg ?? 'PAY_RUNS.DELETE_ERROR');
      this.deleting.set(false);
    }
  }

  viewPayout(id: string): void {
    window.open(`/payouts/${id}`, '_blank');
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 6 }, (_, i) => i);
  }
}
