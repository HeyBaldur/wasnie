import { Component, DestroyRef, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { bindFiltersToUrl } from '../../../shared/state/bind-filters-to-url';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { PayeesStore } from '../state/payees.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { PayeeStatus } from '../models/payee.model';
import { SubscriptionStateService } from '../../subscription/services/subscription-state.service';
import { TierLimitModalService } from '../../../shared/components/tier-limit-modal/tier-limit-modal.service';
import { TIER_LIMITS } from '../../../shared/services/tier-limits';
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
  WsCopyButtonComponent,
  type SegOption,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-payees-list',
  standalone: true,
  imports: [
    AppShellComponent,
    RefreshOnEnterDirective,
    IconComponent,
    RouterLink,
    TranslateModule,
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
    WsCopyButtonComponent,
  ],
  templateUrl: './payees-list.component.html',
  styleUrl: './payees-list.component.scss',
})
export class PayeesListComponent implements OnInit {
  readonly store = inject(PayeesStore);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly subState = inject(SubscriptionStateService);
  private readonly tierLimitModal = inject(TierLimitModalService);

  readonly atPayeesLimit = computed(() => {
    const tier = this.subState.subscription()?.tier ?? 'Free';
    const max = TIER_LIMITS[tier]?.maxPayees ?? -1;
    return max !== -1 && this.store.unfilteredTotal() >= max;
  });

  get payeesTierLimit(): number {
    const tier = this.subState.subscription()?.tier ?? 'Free';
    return TIER_LIMITS[tier]?.maxPayees ?? -1;
  }

  onCreatePayee(): void {
    if (this.atPayeesLimit()) {
      const tier = this.subState.subscription()?.tier ?? 'Free';
      this.tierLimitModal.show({
        tier,
        currentCount: this.store.unfilteredTotal(),
        limit: this.payeesTierLimit,
        entityKey: 'payees',
      });
      return;
    }
    void this.router.navigate(['new'], { relativeTo: this.route });
  }

  readonly PayeeStatus = PayeeStatus;
  readonly openMenuId = signal<string | null>(null);
  readonly menuPosition = signal<{ top?: number; bottom?: number; right: number } | null>(null);

  readonly terminateOpen = signal(false);
  readonly terminateSaving = signal(false);
  readonly pendingTerminateId = signal<string | null>(null);

  readonly deactivateOpen = signal(false);
  readonly deactivateSaving = signal(false);
  readonly pendingDeactivateId = signal<string | null>(null);

  readonly statusOptions: SegOption[] = [
    { value: '', label: 'PAYEES.FILTER_ALL' },
    { value: String(PayeeStatus.Active), label: 'PAYEES.STATUS_ACTIVE' },
    { value: String(PayeeStatus.OnLeave), label: 'PAYEES.STATUS_ON_LEAVE' },
    { value: String(PayeeStatus.Terminated), label: 'PAYEES.STATUS_TERMINATED' },
  ];

  get statusFilter(): string {
    const s = this.store.listParams().status;
    return s === null ? '' : String(s);
  }

  set statusFilter(value: string) {
    this.onStatusFilter(value === '' ? null : value as PayeeStatus);
  }

  ngOnInit(): void {
    // SUBSCRIBE, don't snapshot — see bindFiltersToUrl. Loop-safe: this screen never writes filter
    // params to the URL, so re-applying cannot re-trigger itself.
    bindFiltersToUrl(this.route, this.destroyRef, {
      // Authoritative: an absent or bogus ?status= means "no status filter", not "keep the old one".
      apply: qp => this.store.setStatus(
        ['Active', 'OnLeave', 'Terminated'].includes(qp['status']) ? (qp['status'] as PayeeStatus) : null),
      // This screen's default is the unfiltered list. `search` is not carried in the URL, so it is
      // deliberately left alone.
      reset: () => this.store.setStatus(null),
    });
    // First load handled by the store's constructor effect; re-entry refresh by [refreshOnEnter].
  }

  onSearch(value: string): void {
    this.store.setSearch(value);
  }

  onStatusFilter(status: PayeeStatus | null): void {
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
      const right = window.innerWidth - rect.right;
      if (window.innerHeight - rect.bottom < 108) {
        this.menuPosition.set({ bottom: window.innerHeight - rect.top + 4, right });
      } else {
        this.menuPosition.set({ top: rect.bottom + 4, right });
      }
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

  async onMarkActive(payeeId: string): Promise<void> {
    this.closeMenu();
    try {
      await this.store.markAsActive(payeeId);
      this.toast.show('PAYEES.TOAST_MARKED_ACTIVE', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onMarkOnLeave(payeeId: string): Promise<void> {
    this.closeMenu();
    try {
      await this.store.markAsOnLeave(payeeId);
      this.toast.show('PAYEES.TOAST_MARKED_ON_LEAVE', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  onTerminate(payeeId: string): void {
    this.closeMenu();
    this.pendingTerminateId.set(payeeId);
    this.terminateOpen.set(true);
  }

  onDeactivate(payeeId: string): void {
    this.closeMenu();
    this.pendingDeactivateId.set(payeeId);
    this.deactivateOpen.set(true);
  }

  async onConfirmDeactivate(): Promise<void> {
    const id = this.pendingDeactivateId();
    if (!id) return;
    this.deactivateSaving.set(true);
    try {
      await this.store.deactivate(id);
      this.toast.show('PAYEES.TOAST_DEACTIVATED', 'success');
      this.deactivateOpen.set(false);
      this.pendingDeactivateId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.deactivateSaving.set(false);
    }
  }

  async onActivate(payeeId: string): Promise<void> {
    this.closeMenu();
    try {
      await this.store.activate(payeeId);
      this.toast.show('PAYEES.TOAST_ACTIVATED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onConfirmTerminate(): Promise<void> {
    const id = this.pendingTerminateId();
    if (!id) return;
    this.terminateSaving.set(true);
    const today = new Date().toISOString().split('T')[0];
    try {
      await this.store.markAsTerminated(id, today);
      this.toast.show('PAYEES.TOAST_TERMINATED', 'success');
      this.terminateOpen.set(false);
      this.pendingTerminateId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.terminateSaving.set(false);
    }
  }

  payeeStatusVariant(status: PayeeStatus): BadgeVariant {
    switch (status) {
      case PayeeStatus.Active: return 'success';
      case PayeeStatus.OnLeave: return 'warning';
      case PayeeStatus.Terminated: return 'neutral';
    }
  }

  payeeStatusKey(status: PayeeStatus): string {
    switch (status) {
      case PayeeStatus.Active: return 'PAYEES.STATUS_ACTIVE';
      case PayeeStatus.OnLeave: return 'PAYEES.STATUS_ON_LEAVE';
      case PayeeStatus.Terminated: return 'PAYEES.STATUS_TERMINATED';
    }
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }

  initialsFor(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((w) => w[0])
      .join('')
      .toUpperCase();
  }
}
