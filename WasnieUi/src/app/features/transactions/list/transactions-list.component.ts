import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ProcessPendingComponent } from '../process-pending/process-pending.component';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { TransactionsStore } from '../state/transactions.store';
import { Transaction, TransactionStatus } from '../models/transaction.model';
import { AssignPayeeModalComponent } from '../assign-payee-modal/assign-payee-modal.component';
import { ReassignPayeeModalComponent } from '../reassign-payee-modal/reassign-payee-modal.component';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsSegmentedControlComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  WsPaginationComponent,
  type SegOption,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-transactions-list',
  standalone: true,
  imports: [
    AppShellComponent,
    RouterLink,
    TranslateModule,
    HasPermissionPipe,
    HasPermissionDirective,
    IconComponent,
    CurrencyFormatPipe,
    WsButtonComponent,
    WsBadgeComponent,
    WsSegmentedControlComponent,
    WsPageLayoutComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsEmptyStateComponent,
    WsPaginationComponent,
    AssignPayeeModalComponent,
    ReassignPayeeModalComponent,
    ProcessPendingComponent,
  ],
  templateUrl: './transactions-list.component.html',
  styleUrl: './transactions-list.component.scss',
})
export class TransactionsListComponent {
  readonly store = inject(TransactionsStore);

  readonly TransactionStatus = TransactionStatus;

  readonly showProcessPending = computed(() =>
    !!this.store.payeeIdFilter() &&
    !!this.store.dateFromFilter() &&
    !!this.store.dateToFilter()
  );

  readonly assignModalOpen = signal(false);
  readonly reassignModalOpen = signal(false);
  readonly selectedTransaction = signal<Transaction | null>(null);

  openAssign(tx: Transaction): void {
    this.selectedTransaction.set(tx);
    this.assignModalOpen.set(true);
  }

  openReassign(tx: Transaction): void {
    this.selectedTransaction.set(tx);
    this.reassignModalOpen.set(true);
  }

  onModalClosed(): void {
    this.assignModalOpen.set(false);
    this.reassignModalOpen.set(false);
    this.selectedTransaction.set(null);
  }

  isUnassigned(tx: Transaction): boolean {
    return tx.payeeId === null;
  }

  canReassign(tx: Transaction): boolean {
    return tx.payeeId !== null && tx.status !== TransactionStatus.Paid;
  }

  readonly statusOptions: SegOption[] = [
    { value: '', label: 'TRANSACTIONS.FILTER_ALL' },
    { value: TransactionStatus.Pending, label: 'TRANSACTIONS.STATUS_PENDING' },
    { value: TransactionStatus.Eligible, label: 'TRANSACTIONS.STATUS_ELIGIBLE' },
    { value: TransactionStatus.Calculated, label: 'TRANSACTIONS.STATUS_CALCULATED' },
    { value: TransactionStatus.Paid, label: 'TRANSACTIONS.STATUS_PAID' },
    { value: TransactionStatus.Cancelled, label: 'TRANSACTIONS.STATUS_CANCELLED' },
  ];

  get statusFilterValue(): string {
    return this.store.statusFilter() ?? '';
  }

  set statusFilterValue(value: string) {
    this.store.setStatusFilter(value === '' ? null : value as TransactionStatus);
  }

  goToPage(page: number): void {
    this.store.setPage(page);
  }

  goToPageSize(size: number): void {
    this.store.setPageSize(size);
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
    switch (status) {
      case TransactionStatus.Pending: return 'TRANSACTIONS.STATUS_PENDING';
      case TransactionStatus.Eligible: return 'TRANSACTIONS.STATUS_ELIGIBLE';
      case TransactionStatus.Calculated: return 'TRANSACTIONS.STATUS_CALCULATED';
      case TransactionStatus.Paid: return 'TRANSACTIONS.STATUS_PAID';
      case TransactionStatus.Cancelled: return 'TRANSACTIONS.STATUS_CANCELLED';
    }
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
