import { Component, HostListener, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractApiError } from '../../../shared/utils/api-error';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { ModalService } from '../../../shared/modals/modal.service';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { PlanStatus } from '../models/plan.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsBadgeComponent,
  WsSegmentedControlComponent,
  WsPageHeaderComponent,
  WsTableComponent,
  WsTablePaginationComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  type SegOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-plans-list',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    TranslateModule,
    DateFormatPipe,
    WsButtonComponent,
    WsInputComponent,
    WsBadgeComponent,
    WsSegmentedControlComponent,
    WsPageHeaderComponent,
    WsTableComponent,
    WsTablePaginationComponent,
    WsTableEmptyComponent,
    WsEmptyStateComponent,
  ],
  templateUrl: './plans-list.component.html',
  styleUrl: './plans-list.component.scss',
})
export class PlansListComponent implements OnInit {
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly modal = inject(ModalService);

  readonly openMenuId = signal<string | null>(null);
  readonly menuPosition = signal<{ top: number; right: number } | null>(null);

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
    this.store.loadPlans();
  }

  onSearch(value: string): void {
    this.store.updateParams({ search: value, page: 1 });
  }

  onStatusFilter(status: PlanStatus | null): void {
    this.store.updateParams({ status, page: 1 });
  }

  goToPage(page: number): void {
    this.store.updateParams({ page });
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

  async onDelete(planId: string): Promise<void> {
    this.closeMenu();
    const confirmed = await this.modal.confirm({
      title: 'PLANS.CONFIRM_DELETE_TITLE',
      message: 'PLANS.CONFIRM_DELETE_MSG',
      confirmLabel: 'COMMON.DELETE',
      cancelLabel: 'COMMON.CANCEL',
      variant: 'danger',
    });
    if (!confirmed) return;
    try {
      await this.store.deletePlan(planId);
      this.toast.show('PLANS.TOAST_DELETED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onClone(planId: string): Promise<void> {
    this.closeMenu();
    try {
      await this.store.clonePlan(planId);
      this.toast.show('PLANS.TOAST_CLONED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onActivate(planId: string): Promise<void> {
    this.closeMenu();
    const confirmed = await this.modal.confirm({
      title: 'PLANS.CONFIRM_ACTIVATE_TITLE',
      message: 'PLANS.CONFIRM_ACTIVATE_MSG',
      confirmLabel: 'PLANS.ACTION_ACTIVATE',
      cancelLabel: 'COMMON.CANCEL',
      variant: 'default',
    });
    if (!confirmed) return;
    try {
      await this.store.activatePlan(planId);
      this.toast.show('PLANS.TOAST_ACTIVATED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onArchive(planId: string): Promise<void> {
    this.closeMenu();
    const confirmed = await this.modal.confirm({
      title: 'PLANS.CONFIRM_ARCHIVE_TITLE',
      message: 'PLANS.CONFIRM_ARCHIVE_MSG',
      confirmLabel: 'PLANS.ACTION_ARCHIVE',
      cancelLabel: 'COMMON.CANCEL',
      variant: 'danger',
    });
    if (!confirmed) return;
    try {
      await this.store.archivePlan(planId);
      this.toast.show('PLANS.TOAST_ARCHIVED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
