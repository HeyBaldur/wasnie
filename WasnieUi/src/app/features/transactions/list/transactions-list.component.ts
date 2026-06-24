import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { ProcessPendingComponent } from '../process-pending/process-pending.component';
import { TransactionFilterComponent } from '../filter/transaction-filter.component';
import { HubSpotSyncBannerComponent } from '../../integrations/components/hubspot-sync-banner/hubspot-sync-banner.component';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { TransactionsStore, TransactionFilter } from '../state/transactions.store';
import { TransactionsApiService } from '../services/transactions.api.service';
import { Transaction, TransactionStatus } from '../models/transaction.model';
import { AssignPayeeModalComponent } from '../assign-payee-modal/assign-payee-modal.component';
import { ReassignPayeeModalComponent } from '../reassign-payee-modal/reassign-payee-modal.component';
import { VoidTransactionModalComponent } from '../void-transaction-modal/void-transaction-modal.component';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsSegmentedControlComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  WsPaginationComponent,
  WsModalComponent,
  WsInputComponent,
  type SegOption,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-transactions-list',
  standalone: true,
  imports: [
    AppShellComponent,
    RefreshOnEnterDirective,
    FormsModule,
    RouterLink,
    TranslateModule,
    HasPermissionPipe,
    HasPermissionDirective,
    IconComponent,
    CurrencyFormatPipe,
    DateFormatPipe,
    WsButtonComponent,
    WsBadgeComponent,
    WsSegmentedControlComponent,
    WsPageLayoutComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsEmptyStateComponent,
    WsPaginationComponent,
    WsModalComponent,
    WsInputComponent,
    AssignPayeeModalComponent,
    ReassignPayeeModalComponent,
    VoidTransactionModalComponent,
    ProcessPendingComponent,
    TransactionFilterComponent,
    HubSpotSyncBannerComponent,
  ],
  templateUrl: './transactions-list.component.html',
  styleUrl: './transactions-list.component.scss',
})
export class TransactionsListComponent implements OnInit {
  readonly store = inject(TransactionsStore);
  private readonly route = inject(ActivatedRoute);
  private readonly txApi = inject(TransactionsApiService);
  private readonly toast = inject(ToastService);

  // Bulk void
  readonly bulkVoidModalOpen = signal(false);
  readonly bulkVoidReason = signal('');
  readonly bulkVoiding = signal(false);
  readonly bulkVoidCount = signal(0);
  readonly bulkVoidErrors = signal<string[]>([]);

  readonly exporting = signal(false);
  readonly exportError = signal<string | null>(null);

  private static readonly EXPORT_CONFIRM_THRESHOLD = 50_000;

  readonly TransactionStatus = TransactionStatus;

  // ProcessPending surface: shown when exactly one payee + tx date range are set
  readonly showProcessPending = computed(() =>
    this.store.payeeIdsFilter().length === 1 &&
    !!this.store.txDateFromFilter() &&
    !!this.store.txDateToFilter()
  );

  readonly assignModalOpen = signal(false);
  readonly reassignModalOpen = signal(false);
  readonly voidModalOpen = signal(false);
  readonly selectedTransaction = signal<Transaction | null>(null);

  // Tabs: Eligible removed (never set today — see PROJECT_STATUS decision)
  readonly statusOptions: SegOption[] = [
    { value: '', label: 'TRANSACTIONS.FILTER_ALL' },
    { value: TransactionStatus.Pending, label: 'TRANSACTIONS.STATUS_PENDING' },
    { value: TransactionStatus.Calculated, label: 'TRANSACTIONS.STATUS_CALCULATED' },
    { value: TransactionStatus.Paid, label: 'TRANSACTIONS.STATUS_PAID' },
    { value: TransactionStatus.Cancelled, label: 'TRANSACTIONS.STATUS_CANCELLED' },
  ];

  ngOnInit(): void {
    // Restore filter from URL query params on load
    const qp = this.route.snapshot.queryParams as Record<string, string>;
    if (Object.keys(qp).length > 0) {
      this.store.loadFromQueryParams(qp);
    }
  }

  onFilterChange(partial: Partial<TransactionFilter>): void {
    this.store.setFilter(partial);
    this._syncUrl();
  }

  onFilterCleared(): void {
    this.store.clearFilters();
    this._syncUrl();
  }

  private _syncUrl(): void {
    const qp = this.store.toQueryParams();
    const suffix = Object.keys(qp).length > 0 ? '?' + new URLSearchParams(qp).toString() : '';
    window.history.replaceState(null, '', window.location.pathname + suffix);
  }

  // Tab click: sets single status (or clears if All)
  get statusTabValue(): string {
    const s = this.store.statusesFilter();
    return s.length === 1 ? s[0] : '';
  }

  set statusTabValue(value: string) {
    this.store.setStatusTab(value === '' ? null : value as TransactionStatus);
    this._syncUrl();
  }

  openAssign(tx: Transaction): void {
    this.selectedTransaction.set(tx);
    this.assignModalOpen.set(true);
  }

  openReassign(tx: Transaction): void {
    this.selectedTransaction.set(tx);
    this.reassignModalOpen.set(true);
  }

  openVoid(tx: Transaction): void {
    this.selectedTransaction.set(tx);
    this.voidModalOpen.set(true);
  }

  onModalClosed(): void {
    this.assignModalOpen.set(false);
    this.reassignModalOpen.set(false);
    this.voidModalOpen.set(false);
    this.selectedTransaction.set(null);
  }

  isUnassigned(tx: Transaction): boolean { return tx.payeeId === null; }

  canReassign(tx: Transaction): boolean {
    return tx.payeeId !== null
      && tx.status !== TransactionStatus.Paid
      && tx.status !== TransactionStatus.Cancelled;
  }

  canVoid(tx: Transaction): boolean {
    return tx.status === TransactionStatus.Pending;
  }

  goToPage(page: number): void { this.store.setPage(page); }
  goToPageSize(size: number): void { this.store.setPageSize(size); }

  // ── Bulk void ──────────────────────────────────────────────────────────────
  openBulkVoid(): void {
    this.bulkVoidCount.set(this.store.selectedCount());
    this.bulkVoidReason.set('');
    this.bulkVoidErrors.set([]);
    this.bulkVoidModalOpen.set(true);
  }

  async confirmBulkVoid(): Promise<void> {
    const reason = this.bulkVoidReason().trim();
    if (reason.length < 3 || this.bulkVoiding()) return;
    this.bulkVoiding.set(true);
    this.bulkVoidErrors.set([]);
    try {
      const result = await this.store.bulkVoid(reason);
      if (result.errors.length === 0) {
        this.bulkVoidModalOpen.set(false);
        this.toast.show('TRANSACTIONS.BULK_VOID.TOAST_SUCCESS', 'success');
      } else {
        // Some couldn't be voided (paid/calculated/active credits) — keep them visible, never silent.
        this.bulkVoidErrors.set(result.errors);
        this.toast.show(
          result.voidedCount > 0
            ? 'TRANSACTIONS.BULK_VOID.TOAST_PARTIAL'
            : 'TRANSACTIONS.BULK_VOID.TOAST_NONE',
          'warning');
      }
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.bulkVoiding.set(false);
    }
  }

  async onExport(): Promise<void> {
    if (this.exporting()) return;

    const total = this.store.totalCount();
    if (total > TransactionsListComponent.EXPORT_CONFIRM_THRESHOLD) {
      const msg = `This export contains ${total.toLocaleString()} rows and may take a moment. Continue?`;
      if (!window.confirm(msg)) return;
    }

    this.exporting.set(true);
    this.exportError.set(null);
    try {
      const blob = await firstValueFrom(this.txApi.exportToExcel(this.store.toExportFilter()));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `transactions-export-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      this.exportError.set('TRANSACTIONS.EXPORT.ERROR');
    } finally {
      this.exporting.set(false);
    }
  }

  statusVariant(status: TransactionStatus): BadgeVariant {
    switch (status) {
      case TransactionStatus.Pending: return 'warning';
      case TransactionStatus.Eligible: return 'info';
      case TransactionStatus.Calculated: return 'brand';
      case TransactionStatus.Paid: return 'success';
      case TransactionStatus.Cancelled: return 'neutral';
    }
  }

  statusKey(status: TransactionStatus): string {
    return `TRANSACTIONS.STATUS_${status.toUpperCase()}`;
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
