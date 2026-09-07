import { Component, DestroyRef, HostListener, OnInit, inject, signal } from '@angular/core';
import { createRowMenu } from '../../../shared/utils/row-menu';
import { bindFiltersToUrl } from '../../../shared/state/bind-filters-to-url';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { QuotasStore } from '../state/quotas.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { QuotaStatusVariantPipe, QuotaStatusLabelPipe, QuotaPeriodExpiredPipe } from '../../../shared/pipes/quota-status.pipe';
import { QuotaStatus } from '../models/quota.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsBadgeComponent,
  WsSegmentedControlComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsClickableRowDirective,
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
    RefreshOnEnterDirective,
    IconComponent,
    RouterLink,
    TranslateModule,
    CurrencyFormatPipe,
    DateFormatPipe,
    QuotaStatusVariantPipe,
    QuotaStatusLabelPipe,
    QuotaPeriodExpiredPipe,
    HasPermissionDirective,
    HasPermissionPipe,
    WsButtonComponent,
    WsInputComponent,
    WsBadgeComponent,
    WsSegmentedControlComponent,
    WsPageLayoutComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsClickableRowDirective,
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
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  // ★ The "⋯" menu follows its row while the page scrolls — see RowMenuController. These four
  // list screens each carried an identical measure-once copy, and so the identical defect.
  private readonly rowMenu = createRowMenu();
  readonly openMenuId = this.rowMenu.openMenuId;
  readonly menuPosition = this.rowMenu.menuPosition;

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
    // SUBSCRIBE, don't snapshot — see bindFiltersToUrl. Loop-safe: this screen never writes filter
    // params to the URL, so re-applying cannot re-trigger itself.
    bindFiltersToUrl(this.route, this.destroyRef, {
      // Authoritative: an absent or bogus ?status= means "no status filter", not "keep the old one".
      apply: qp => this.store.setStatus(
        ['Draft', 'Active', 'Closed'].includes(qp['status']) ? (qp['status'] as QuotaStatus) : null),
      // This screen's default is the unfiltered list. `search` is not carried in the URL, so it is
      // deliberately left alone.
      reset: () => this.store.setStatus(null),
    });
    // First load handled by the store's constructor effect; re-entry refresh by [refreshOnEnter].
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
    this.rowMenu.toggle(id, event);
  }

  closeMenu(): void {
    this.rowMenu.close();
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

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }

  initialsFor(name: string): string {
    return name.split(' ').slice(0, 2).map((w) => w[0]).join('').toUpperCase();
  }
}
