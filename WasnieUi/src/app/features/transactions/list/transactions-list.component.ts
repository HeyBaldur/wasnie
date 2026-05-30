import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { TransactionsStore } from '../state/transactions.store';
import { TransactionStatus } from '../models/transaction.model';
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
  ],
  templateUrl: './transactions-list.component.html',
  styleUrl: './transactions-list.component.scss',
})
export class TransactionsListComponent {
  readonly store = inject(TransactionsStore);

  readonly TransactionStatus = TransactionStatus;

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
