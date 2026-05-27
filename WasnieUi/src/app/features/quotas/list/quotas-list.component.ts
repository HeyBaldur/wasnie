import { Component, HostListener, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { QuotasStore } from '../state/quotas.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { QuotaStatus } from '../models/quota.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsBadgeComponent,
  WsSegmentedControlComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  WsConfirmationModalComponent,
  WsPaginationComponent,
  type SegOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-quotas-list',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    TranslateModule,
    CurrencyFormatPipe,
    DateFormatPipe,
    HasPermissionDirective,
    HasPermissionPipe,
    WsButtonComponent,
    WsInputComponent,
    WsBadgeComponent,
    WsSegmentedControlComponent,
    WsPageLayoutComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsEmptyStateComponent,
    WsConfirmationModalComponent,
    WsPaginationComponent,
  ],
  templateUrl: './quotas-list.component.html',
  styleUrl: './quotas-list.component.scss',
})
export class QuotasListComponent implements OnInit {
  readonly store = inject(QuotasStore);
  private readonly toast = inject(ToastService);

  readonly openMenuId = signal<string | null>(null);
  readonly menuPosition = signal<{ top: number; right: number } | null>(null);

  readonly closeOpen = signal(false);
  readonly closeSaving = signal(false);
  readonly pendingCloseId = signal<string | null>(null);

  readonly statusOptions: SegOption[] = [
    { value: '', label: 'QUOTAS.FILTER_ALL' },
    { value: 'Draft', label: 'QUOTAS.STATUS_DRAFT' },
    { value: 'Active', label: 'QUOTAS.STATUS_ACTIVE' },
    { value: 'Closed', label: 'QUOTAS.STATUS_CLOSED' },
  ];

  get statusFilter(): string {
    return this.store.listParams().status ?? '';
  }

  set statusFilter(value: string) {
    this.onStatusFilter(value === '' ? null : (value as QuotaStatus));
  }

  ngOnInit(): void {
    this.store.loadQuotas();
  }

  onSearch(value: string): void {
    this.store.setSearch(value);
  }

  onStatusFilter(status: QuotaStatus | null): void {
    this.store.setStatus(status);
  }

  goToPage(page: number): void {
    this.store.setPage(page);
  }

  goToPageSize(size: number): void {
    this.store.setPageSize(size);
  }

  toggleMenu(id: string, event: Event): void {
    event.stopPropagation();
    const isOpening = this.openMenuId() !== id;
    this.openMenuId.update((cur) => (cur === id ? null : id));
    if (isOpening) {
      const btn = event.currentTarget as HTMLElement;
      const rect = btn.getBoundingClientRect();
      this.menuPosition.set({ top: rect.bottom + 4, right: window.innerWidth - rect.right });
    } else {
      this.menuPosition.set(null);
    }
  }

  closeMenu(): void {
    this.openMenuId.set(null);
    this.menuPosition.set(null);
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.closeMenu();
  }

  async onActivate(quotaId: string): Promise<void> {
    this.closeMenu();
    try {
      await this.store.activateQuota(quotaId);
      this.toast.show('QUOTAS.TOAST_ACTIVATED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  onClose(quotaId: string): void {
    this.closeMenu();
    this.pendingCloseId.set(quotaId);
    this.closeOpen.set(true);
  }

  async onConfirmClose(): Promise<void> {
    const id = this.pendingCloseId();
    if (!id) return;
    this.closeSaving.set(true);
    try {
      await this.store.closeQuota(id);
      this.toast.show('QUOTAS.TOAST_CLOSED', 'success');
      this.closeOpen.set(false);
      this.pendingCloseId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.closeSaving.set(false);
    }
  }

  statusVariant(status: QuotaStatus): 'neutral' | 'success' | 'warning' {
    switch (status) {
      case 'Active': return 'success';
      case 'Draft': return 'neutral';
      case 'Closed': return 'warning';
    }
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }

  initialsFor(name: string): string {
    return name.split(' ').slice(0, 2).map((w) => w[0]).join('').toUpperCase();
  }
}
