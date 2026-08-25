import { Component, DestroyRef, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { bindFiltersToUrl } from '../../../shared/state/bind-filters-to-url';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractApiError } from '../../../shared/utils/api-error';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { PlanStatus } from '../models/plan.model';
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
  type SegOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-plans-list',
  standalone: true,
  imports: [
    AppShellComponent,
    RefreshOnEnterDirective,
    IconComponent,
    RouterLink,
    TranslateModule,
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
    WsClickableRowDirective,
    WsEmptyStateComponent,
    WsConfirmationModalComponent,
    WsPaginationComponent,
  ],
  templateUrl: './plans-list.component.html',
  styleUrl: './plans-list.component.scss',
})
export class PlansListComponent implements OnInit {
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly subState = inject(SubscriptionStateService);
  private readonly tierLimitModal = inject(TierLimitModalService);

  readonly atPlansLimit = computed(() => {
    const tier = this.subState.subscription()?.tier ?? 'Free';
    const max = TIER_LIMITS[tier]?.maxPlans ?? -1;
    return max !== -1 && this.store.unfilteredTotal() >= max;
  });

  get plansTierLimit(): number {
    const tier = this.subState.subscription()?.tier ?? 'Free';
    return TIER_LIMITS[tier]?.maxPlans ?? -1;
  }

  onCreatePlan(): void {
    if (this.atPlansLimit()) {
      const tier = this.subState.subscription()?.tier ?? 'Free';
      this.tierLimitModal.show({
        tier,
        currentCount: this.store.unfilteredTotal(),
        limit: this.plansTierLimit,
        entityKey: 'plans',
      });
      return;
    }
    void this.router.navigate(['new'], { relativeTo: this.route });
  }

  readonly openMenuId = signal<string | null>(null);
  readonly menuPosition = signal<{ top?: number; bottom?: number; right: number } | null>(null);

  readonly deleteOpen = signal(false);
  readonly deleteSaving = signal(false);
  readonly pendingDeleteId = signal<string | null>(null);

  readonly activateOpen = signal(false);
  readonly activateSaving = signal(false);
  readonly pendingActivateId = signal<string | null>(null);

  readonly archiveOpen = signal(false);
  readonly archiveSaving = signal(false);
  readonly pendingArchiveId = signal<string | null>(null);

  readonly statusOptions: SegOption[] = [
    { value: '', label: 'PLANS.FILTER_ALL' },
    { value: 'Draft', label: 'PLANS.STATUS_DRAFT' },
    { value: 'Active', label: 'PLANS.STATUS_ACTIVE' },
    { value: 'Archived', label: 'PLANS.STATUS_ARCHIVED' },
  ];

  get statusFilter(): string {
    return this.store.listParams().status ?? '';
  }

  set statusFilter(value: string) {
    this.onStatusFilter(value === '' ? null : value as PlanStatus);
  }

  ngOnInit(): void {
    // SUBSCRIBE, don't snapshot — see bindFiltersToUrl. Loop-safe: this screen never writes filter
    // params to the URL, so re-applying cannot re-trigger itself.
    bindFiltersToUrl(this.route, this.destroyRef, {
      // Authoritative: an absent or bogus ?status= means "no status filter", not "keep the old one".
      apply: qp => this.store.setStatus(
        ['Draft', 'Active', 'Archived'].includes(qp['status']) ? (qp['status'] as PlanStatus) : null),
      // This screen's default is the unfiltered list. `search` is not carried in the URL, so it is
      // deliberately left alone.
      reset: () => this.store.setStatus(null),
    });
    // First load handled by the store's constructor effect; re-entry refresh by [refreshOnEnter].
  }

  onSearch(value: string): void {
    this.store.setSearch(value);
  }

  onStatusFilter(status: PlanStatus | null): void {
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

  onDelete(planId: string): void {
    this.closeMenu();
    this.pendingDeleteId.set(planId);
    this.deleteOpen.set(true);
  }

  async onConfirmDelete(): Promise<void> {
    const id = this.pendingDeleteId();
    if (!id) return;
    this.deleteSaving.set(true);
    try {
      await this.store.deletePlan(id);
      this.toast.show('PLANS.TOAST_DELETED', 'success');
      this.deleteOpen.set(false);
      this.pendingDeleteId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.deleteSaving.set(false);
    }
  }

  async onClone(planId: string): Promise<void> {
    this.closeMenu();
    if (this.atPlansLimit()) {
      const tier = this.subState.subscription()?.tier ?? 'Free';
      this.tierLimitModal.show({
        tier,
        currentCount: this.store.unfilteredTotal(),
        limit: this.plansTierLimit,
        entityKey: 'plans',
      });
      return;
    }
    try {
      await this.store.clonePlan(planId);
      this.toast.show('PLANS.TOAST_CLONED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  onActivate(planId: string): void {
    this.closeMenu();
    this.pendingActivateId.set(planId);
    this.activateOpen.set(true);
  }

  async onConfirmActivate(): Promise<void> {
    const id = this.pendingActivateId();
    if (!id) return;
    this.activateSaving.set(true);
    try {
      await this.store.activatePlan(id);
      this.toast.show('PLANS.TOAST_ACTIVATED', 'success');
      this.activateOpen.set(false);
      this.pendingActivateId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.activateSaving.set(false);
    }
  }

  onArchive(planId: string): void {
    this.closeMenu();
    this.pendingArchiveId.set(planId);
    this.archiveOpen.set(true);
  }

  async onConfirmArchive(): Promise<void> {
    const id = this.pendingArchiveId();
    if (!id) return;
    this.archiveSaving.set(true);
    try {
      await this.store.archivePlan(id);
      this.toast.show('PLANS.TOAST_ARCHIVED', 'success');
      this.archiveOpen.set(false);
      this.pendingArchiveId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.archiveSaving.set(false);
    }
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
